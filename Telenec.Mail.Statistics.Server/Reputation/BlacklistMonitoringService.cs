
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(
    "Telenec.Mail.Statistics.Tests")]

namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Atomar veröffentlichter Monitoring-Zustand.
///
/// Result bleibt für die bisherige PSBL-API erhalten.
/// Results enthält die Ergebnisse aller registrierten
/// Blacklist-Prüfer.
/// </summary>
public sealed record BlacklistMonitoringSnapshot(
    string Hostname,
    string ExpectedIpv4,
    IReadOnlyList<string> ResolvedIpv4Addresses,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessfulCheckUtc,
    BlacklistCheckResult Result)
{
    public IReadOnlyDictionary<string, BlacklistCheckResult>
        Results
    { get; init; } =
        new ReadOnlyDictionary<string, BlacklistCheckResult>(
            new Dictionary<string, BlacklistCheckResult>(
                StringComparer.Ordinal));
}

/// <summary>
/// Serverseitiger, manuell ausgelöster
/// Blacklist-Prüfdienst.
///
/// Beim Serverstart erfolgen keine DNSBL-Abfragen.
///
/// GetSnapshot liest nur den letzten Zustand.
/// CheckNowAsync startet eine neue Prüfung.
///
/// Gleichzeitige Prüfungen werden verhindert.
/// </summary>
public sealed class BlacklistMonitoringService : IDisposable
{
    private readonly SemaphoreSlim _checkGate =
        new(1, 1);

    private readonly TimeProvider _timeProvider;

    private readonly ILogger<BlacklistMonitoringService>
        _logger;

    private readonly BlacklistBatchRunner _batchRunner =
        new();

    private readonly IReadOnlyCollection<BlacklistCheckJob>
        _jobs;

    private readonly string _hostname;

    private readonly string _expectedIpv4;

    /*
     * Der normale Server verwendet RunCheckAsync.
     *
     * Für Regressionstests kann intern eine
     * kontrollierte Prüfoperation eingesetzt werden.
     *
     * Damit testen wir echte Überlappungen
     * ohne DNS-Anfragen.
     */
    private readonly Func<CancellationToken, Task>
        _executeCheck;

    private BlacklistMonitoringSnapshot _snapshot;

    public BlacklistMonitoringService(
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<BlacklistMonitoringService> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;

        /*
         * Im Produktivbetrieb wird ausschließlich
         * die reguläre Prüfoperation verwendet.
         */
        _executeCheck = RunCheckAsync;

        _hostname =
            configuration["Reputation:Hostname"]
            ?? "mail.necnet.de";

        _expectedIpv4 =
            configuration["Reputation:ExpectedIpv4"]
            ?? "80.79.237.70";

        /*
         * Freigegebene DNSBL-Anbieter.
         *
         * PSBL wird separat registriert.
         *
         * Insgesamt acht aktive Prüfer.
         *
         * SpamCop und alle weiteren Quellen
         * bleiben bis zur ausdrücklichen
         * Freigabe deaktiviert.
         */

        var approvedDnsblIds =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "0spam",
                "0spam-rbl",
                "drone-bl",
                "uceprotectl1",
                "blocklist-de",
                "uceprotectl2",
                "uceprotectl3"
            };

        var approvedProfiles =
            DnsBlacklistProviderProfiles.FirstBatch
                .Concat(
                    DnsBlacklistProviderProfiles.SecondBatch)
                .Where(profile =>
                    approvedDnsblIds.Contains(
                        profile.BlacklistId))
                .ToArray();

        var actualIds =
            approvedProfiles
                .Select(profile => profile.BlacklistId)
                .ToHashSet(StringComparer.Ordinal);

        if (approvedProfiles.Length != approvedDnsblIds.Count
            || !actualIds.SetEquals(approvedDnsblIds))
        {
            throw new InvalidOperationException(
                "Die freigegebene DNSBL-Anbietergruppe " +
                "ist unvollständig oder enthält Duplikate.");
        }

        /*
         * Jeder DNSBL-Checker erhält sein
         * eigenes freigegebenes Anbieterprofil.
         */

        var dnsblJobs =
            approvedProfiles
                .Select(profile =>
                {
                    var checker =
                        new DnsBlacklistChecker(profile);

                    return new BlacklistCheckJob(
                        profile.BlacklistId,
                        checker.CheckAsync);
                })
                .ToArray();

        /*
         * PSBL bleibt an erster Stelle.
         * Die bisherige Implementierung
         * wird nicht verändert.
         */

        var psblChecker = new PsblBlacklistChecker();

        _jobs = Array.AsReadOnly(
            new[]
            {
                new BlacklistCheckJob(
                    "psbl",
                    psblChecker.CheckAsync)
            }
            .Concat(dnsblJobs)
            .ToArray());

        /*
         * Kein Check beim Programmstart.
         *
         * Alle Prüfer beginnen mit NotChecked.
         */

        var initialResults =
            CreateResultCollection(
                _jobs.Select(job => new BlacklistCheckResult
                {
                    BlacklistId = job.BlacklistId,
                    CheckedTarget = _expectedIpv4,
                    Status = BlacklistCheckStatus.NotChecked
                }));

        _snapshot = new BlacklistMonitoringSnapshot(
            Hostname: _hostname,
            ExpectedIpv4: _expectedIpv4,
            ResolvedIpv4Addresses: Array.Empty<string>(),
            LastAttemptUtc: null,
            LastSuccessfulCheckUtc: null,
            Result: initialResults["psbl"])
        {
            Results = initialResults
        };
    }

    /// <summary>
    /// Interner Konstruktor für Regressionstests.
    ///
    /// Die simulierte Operation ersetzt nur
    /// die eigentliche Prüfung.
    ///
    /// Die Synchronisierung in CheckNowAsync
    /// bleibt vollständig dieselbe wie
    /// im Produktivbetrieb.
    ///
    /// Dieser Konstruktor ist für die
    /// Serveranwendung nicht öffentlich.
    /// </summary>
    internal BlacklistMonitoringService(
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<BlacklistMonitoringService> logger,
        Func<CancellationToken, Task> testCheck)
        : this(configuration, timeProvider, logger)
    {
        _executeCheck =
            testCheck
            ?? throw new ArgumentNullException(
                nameof(testCheck));
    }

    /// <summary>
    /// Liest den letzten veröffentlichten Zustand.
    ///
    /// Führt niemals DNS-Abfragen durch.
    /// </summary>
    public BlacklistMonitoringSnapshot GetSnapshot()
    {
        return Volatile.Read(ref _snapshot);
    }

    /// <summary>
    /// Startet genau einen Prüfdurchlauf.
    ///
    /// true:
    /// Die Prüfoperation wurde ausgeführt.
    ///
    /// false:
    /// Eine andere Prüfoperation läuft bereits.
    ///
    /// Der fachliche Erfolg einzelner Anbieter
    /// wird über den Snapshot bewertet.
    /// </summary>
    public async Task<bool> CheckNowAsync(
        CancellationToken cancellationToken = default)
    {
        /*
         * Sofort ablehnen, wenn bereits
         * eine Prüfung läuft.
         *
         * Es entsteht keine Warteschlange
         * für weitere Prüfdurchläufe.
         */

        if (!await _checkGate.WaitAsync(
                0,
                cancellationToken))
        {
            return false;
        }

        try
        {
            /*
             * Im Produktivbetrieb wird RunCheckAsync
             * ausgeführt.
             *
             * Regressionstests können stattdessen
             * eine kontrolliert blockierte Operation
             * einsetzen.
             */
            await _executeCheck(cancellationToken);

            return true;
        }
        finally
        {
            /*
             * Die Sperre wird auch nach einem
             * Fehler oder Abbruch freigegeben.
             */
            _checkGate.Release();
        }
    }

    /// <summary>
    /// Führt einen vollständigen Prüfdurchlauf aus.
    ///
    /// Vor den Blacklist-Abfragen wird geprüft,
    /// ob die erwartete Versand-IP zur
    /// DNS-Auflösung des Mailservers passt.
    ///
    /// Die Ergebnisse werden atomar veröffentlicht.
    /// </summary>
    private async Task RunCheckAsync(
        CancellationToken cancellationToken)
    {
        var startedAtUtc =
            _timeProvider.GetUtcNow();

        IReadOnlyList<string> resolvedAddresses =
            Array.Empty<string>();

        IReadOnlyDictionary<string, BlacklistCheckResult>
            results;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IPAddress.TryParse(
                    _expectedIpv4,
                    out var expectedAddress)
                || expectedAddress.AddressFamily
                    != AddressFamily.InterNetwork)
            {
                results = CreateFailureResults(
                    startedAtUtc,
                    "Die konfigurierte Prüf-IP ist " +
                    "keine gültige IPv4-Adresse.");
            }
            else
            {
                var addresses =
                    await Dns.GetHostAddressesAsync(
                        _hostname,
                        AddressFamily.InterNetwork,
                        cancellationToken);

                resolvedAddresses = Array.AsReadOnly(
                    addresses
                        .Select(address => address.ToString())
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(
                            address => address,
                            StringComparer.Ordinal)
                        .ToArray());

                if (!addresses.Contains(expectedAddress))
                {
                    results = CreateFailureResults(
                        startedAtUtc,
                        "Die erwartete Versand-IP wurde bei der " +
                        "DNS-Auflösung des konfigurierten Hosts " +
                        "nicht gefunden.");
                }
                else
                {
                    /*
                     * Gemeinsamer Batch für alle
                     * acht freigegebenen Anbieter.
                     *
                     * Der Batch-Runner übernimmt
                     * Parallelisierung und Zeitlimits.
                     */

                    results =
                        await _batchRunner.RunAsync(
                            _jobs,
                            _expectedIpv4,
                            cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Blacklist-Prüfung für {Hostname} fehlgeschlagen.",
                _hostname);

            results = CreateFailureResults(
                startedAtUtc,
                "Die DNS-Auflösung oder Blacklist-Prüfung " +
                "ist fehlgeschlagen: " +
                exception.Message);
        }

        /*
         * PSBL bleibt das rückwärtskompatible
         * Einzelprüfergebnis.
         *
         * Results enthält alle Anbieter.
         */

        var psblResult = results["psbl"];

        var previousSnapshot = GetSnapshot();

        /*
         * Die bisherige Semantik bleibt vorläufig
         * erhalten:
         *
         * LastSuccessfulCheckUtc bezeichnet
         * weiterhin eine erfolgreiche PSBL-Prüfung.
         */

        var lastSuccessfulCheckUtc =
            psblResult.IsConclusive
                ? psblResult.CompletedAtUtc
                    ?? _timeProvider.GetUtcNow()
                : previousSnapshot.LastSuccessfulCheckUtc;

        var newSnapshot =
            new BlacklistMonitoringSnapshot(
                Hostname: _hostname,
                ExpectedIpv4: _expectedIpv4,
                ResolvedIpv4Addresses: resolvedAddresses,
                LastAttemptUtc: startedAtUtc,
                LastSuccessfulCheckUtc: lastSuccessfulCheckUtc,
                Result: psblResult)
            {
                Results = results
            };

        Volatile.Write(
            ref _snapshot,
            newSnapshot);

        var listedCount =
            results.Values.Count(result =>
                result.Status ==
                    BlacklistCheckStatus.Listed);

        var notListedCount =
            results.Values.Count(result =>
                result.Status ==
                    BlacklistCheckStatus.NotListed);

        var warningCount =
            results.Count - listedCount - notListedCount;

        _logger.LogInformation(
            "Manuelle Blacklist-Prüfung abgeschlossen. " +
            "Host: {Hostname}, IP: {IpAddress}, " +
            "Anbieter: {ProviderCount}, " +
            "Nicht gelistet: {NotListedCount}, " +
            "Gelistet: {ListedCount}, " +
            "Unklar/Fehler: {WarningCount}.",
            _hostname,
            _expectedIpv4,
            results.Count,
            notListedCount,
            listedCount,
            warningCount);
    }

    /// <summary>
    /// Erstellt Fehlerergebnisse für alle Prüfer.
    ///
    /// Eine fehlgeschlagene Vorprüfung darf
    /// niemals als NotListed erscheinen.
    /// </summary>
    private IReadOnlyDictionary<string, BlacklistCheckResult>
        CreateFailureResults(
            DateTimeOffset startedAtUtc,
            string errorMessage)
    {
        return CreateResultCollection(
            _jobs.Select(job => new BlacklistCheckResult
            {
                BlacklistId = job.BlacklistId,
                CheckedTarget = _expectedIpv4,
                Status = BlacklistCheckStatus.Failed,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = _timeProvider.GetUtcNow(),
                ErrorMessage = errorMessage
            }));
    }

    /// <summary>
    /// Erstellt eine schreibgeschützte Sammlung
    /// mit eindeutigen Anbieterkennungen.
    /// </summary>
    private static IReadOnlyDictionary<string, BlacklistCheckResult>
        CreateResultCollection(
            IEnumerable<BlacklistCheckResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var dictionary =
            new Dictionary<string, BlacklistCheckResult>(
                StringComparer.Ordinal);

        foreach (var result in results)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (string.IsNullOrWhiteSpace(
                    result.BlacklistId))
            {
                throw new InvalidOperationException(
                    "Ein Blacklist-Ergebnis besitzt " +
                    "keine gültige Kennung.");
            }

            if (!dictionary.TryAdd(
                    result.BlacklistId,
                    result))
            {
                throw new InvalidOperationException(
                    "Ein Blacklist-Ergebnis wurde " +
                    "mehrfach registriert: " +
                    result.BlacklistId);
            }
        }

        return new ReadOnlyDictionary<
            string,
            BlacklistCheckResult>(dictionary);
    }

    public void Dispose()
    {
        _checkGate.Dispose();
    }
}
