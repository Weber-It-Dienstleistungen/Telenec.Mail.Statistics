
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;

namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Atomar veröffentlichter Monitoring-Zustand.
///
/// Result bleibt für die bestehende PSBL-API erhalten.
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
/// Serverseitiger Blacklist-Monitoring-Dienst.
///
/// Die Prüfungen erfolgen beim Serverstart und
/// anschließend alle 15 Minuten.
///
/// Der Admin-API-Aufruf löst keine eigenen
/// externen Blacklist-Abfragen aus.
///
/// Alle Ergebnisse werden gemeinsam veröffentlicht.
/// </summary>
public sealed class BlacklistMonitoringService : BackgroundService
{
    private static readonly TimeSpan CheckInterval =
        TimeSpan.FromMinutes(15);

    private readonly TimeProvider _timeProvider;

    private readonly ILogger<BlacklistMonitoringService> _logger;

    private readonly BlacklistBatchRunner _batchRunner =
        new();

    private readonly IReadOnlyCollection<BlacklistCheckJob> _jobs;

    private readonly string _hostname;

    private readonly string _expectedIpv4;

    private BlacklistMonitoringSnapshot _snapshot;

    public BlacklistMonitoringService(
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<BlacklistMonitoringService> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;

        _hostname =
            configuration["Reputation:Hostname"]
            ?? "mail.necnet.de";

        _expectedIpv4 =
            configuration["Reputation:ExpectedIpv4"]
            ?? "80.79.237.70";

        /*
         * Freigegebene aktive DNSBL-Profile:
         *
         * Erste Gruppe:
         * - 0spam
         * - 0spam RBL
         * - DroneBL
         * - UCEPROTECT Level 1
         *
         * Zweite Gruppe:
         * - blocklist.de
         * - UCEPROTECT Level 2
         * - UCEPROTECT Level 3
         *
         * PSBL wird separat registriert.
         *
         * Damit sind insgesamt acht
         * Reputationslisten aktiv.
         *
         * DroneBL wurde nach erfolgreicher
         * Positiv- und Negativkontrolle
         * auf dem Ubuntu-Server freigegeben.
         *
         * SpamCop bleibt deaktiviert, bis die
         * Nutzungsbedingungen für unseren
         * Anwendungsfall abschließend geprüft sind.
         *
         * Andere Profile werden niemals
         * automatisch aktiviert.
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

        /*
         * Beide vorbereiteten Anbietergruppen
         * bilden den gemeinsamen Profilkatalog.
         *
         * Nur explizit freigegebene Kennungen
         * gelangen in das produktive Monitoring.
         */

        var approvedProfiles =
            DnsBlacklistProviderProfiles.FirstBatch
                .Concat(
                    DnsBlacklistProviderProfiles.SecondBatch)
                .Where(profile =>
                    approvedDnsblIds.Contains(
                        profile.BlacklistId))
                .ToArray();

        /*
         * Die Freigabeliste muss vollständig
         * und ohne Duplikate umgesetzt sein.
         *
         * Fehlende oder doppelte Profile führen
         * zu einem Startfehler statt zu einer
         * stillschweigend unvollständigen Prüfung.
         */

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
         * Jeder DNSBL-Checker besitzt ein
         * eigenes, unveränderliches Anbieterprofil.
         *
         * Der Batch-Runner entscheidet über
         * Parallelisierung und Zeitlimits.
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
         *
         * Die bisherige PSBL-Implementierung
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
         * Alle registrierten Anbieter erhalten
         * zunächst den Status NotChecked.
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
    /// Gibt den zuletzt veröffentlichten
    /// Monitoring-Zustand zurück.
    /// </summary>
    public BlacklistMonitoringSnapshot GetSnapshot()
    {
        return Volatile.Read(ref _snapshot);
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            await RunCheckAsync(stoppingToken);

            using var timer = new PeriodicTimer(
                CheckInterval,
                _timeProvider);

            while (await timer.WaitForNextTickAsync(
                       stoppingToken))
            {
                await RunCheckAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Reguläres Beenden des Dienstes.
        }
    }

    /// <summary>
    /// Führt einen Monitoring-Durchlauf aus.
    ///
    /// Vor den Blacklist-Abfragen wird geprüft,
    /// ob die konfigurierte Versand-IP zur
    /// DNS-Auflösung des Mailservers passt.
    ///
    /// Erst danach startet der Batch-Runner.
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
                     * Ein gemeinsamer Batch für
                     * alle acht Anbieter.
                     *
                     * Jeder Anbieter liefert sein
                     * eigenes Prüfergebnis.
                     *
                     * Fehler einzelner Anbieter
                     * werden durch den Batch-Runner
                     * isoliert behandelt.
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
                "Blacklist-Monitoring für {Hostname} fehlgeschlagen.",
                _hostname);

            results = CreateFailureResults(
                startedAtUtc,
                "Die DNS-Auflösung oder Blacklist-Prüfung " +
                "ist fehlgeschlagen: " +
                exception.Message);
        }

        /*
         * Rückwärtskompatibilität:
         *
         * Die bisherige Result-Eigenschaft
         * bezeichnet weiterhin ausschließlich
         * PSBL.
         *
         * Die Results-Sammlung enthält alle
         * acht aktiven Anbieter.
         */

        var psblResult = results["psbl"];

        var previousSnapshot = GetSnapshot();

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

        /*
         * Atomare Veröffentlichung.
         */

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
            "Blacklist-Monitoring abgeschlossen. " +
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
    /// Erstellt Fehlerergebnisse für alle
    /// registrierten Anbieter.
    ///
    /// Eine fehlgeschlagene Vorprüfung
    /// darf niemals als NotListed erscheinen.
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
    /// Erstellt eine schreibgeschützte
    /// Sammlung mit eindeutigen Kennungen.
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
}
