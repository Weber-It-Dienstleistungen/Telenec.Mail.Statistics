
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
/// Serverseitiger Monitoring-Dienst.
///
/// Ablauf:
/// 1. Konfiguration und DNS-Auflösung prüfen.
/// 2. Alle registrierten IP-Blacklist-Prüfer
///    über den gemeinsamen Batch-Runner ausführen.
/// 3. Ergebnisse gesammelt veröffentlichen.
///
/// Dashboard-Aufrufe lösen keine DNSBL-Prüfungen aus.
/// </summary>
public sealed class BlacklistMonitoringService : BackgroundService
{
    private static readonly TimeSpan CheckInterval =
        TimeSpan.FromMinutes(15);

    private readonly TimeProvider _timeProvider;

    private readonly ILogger<BlacklistMonitoringService> _logger;

    private readonly PsblBlacklistChecker _psblChecker =
        new();

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
         * Zentrale Registrierung der aktiven Prüfer.
         *
         * Derzeit ist ausschließlich PSBL freigegeben.
         *
         * Weitere Anbieter werden erst nach Prüfung
         * ihrer technischen Eigenschaften und
         * Nutzungsbedingungen aufgenommen.
         *
         * Die Registrierung selbst startet
         * noch keine externe Abfrage.
         */
        _jobs = Array.AsReadOnly(
            new[]
            {
                new BlacklistCheckJob(
                    "psbl",
                    _psblChecker.CheckAsync)
            });

        /*
         * Initialzustand aller registrierten Prüfer.
         *
         * Noch nicht durchgeführte Prüfungen erhalten
         * ausdrücklich den Status NotChecked.
         */
        var initialResults = CreateResultCollection(
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
    /// Liefert den zuletzt vollständig veröffentlichten
    /// Monitoring-Snapshot ohne weitere DNS-Abfrage.
    /// </summary>
    public BlacklistMonitoringSnapshot GetSnapshot()
    {
        return Volatile.Read(ref _snapshot);
    }

    /// <summary>
    /// Erste Prüfung direkt nach dem Serverstart.
    /// Anschließend Wiederholung alle 15 Minuten.
    ///
    /// Durchläufe überschneiden sich nicht.
    /// </summary>
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
            // Reguläres Beenden des Hintergrunddienstes.
        }
    }

    /// <summary>
    /// Gemeinsamer Monitoring-Durchlauf.
    ///
    /// Die erwartete Versand-IP muss zunächst
    /// zur DNS-Auflösung des Mailservers passen.
    ///
    /// Bei einer DNS-Abweichung werden keine
    /// externen Blacklists abgefragt.
    ///
    /// Stattdessen bekommen alle registrierten
    /// Prüfer einen fachlichen Fehlerstatus.
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
                    "Die konfigurierte Prüf-IP ist keine gültige IPv4-Adresse.");
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
                     * Die eigentlichen Blacklist-Abfragen
                     * laufen jetzt ausschließlich über
                     * unseren gemeinsamen Batch-Runner.
                     *
                     * Derzeit wird genau ein Job ausgeführt:
                     * PSBL.
                     *
                     * Später können weitere freigegebene
                     * IP-DNSBL-Checker registriert werden.
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
            // Ein ausdrücklich angeforderter Abbruch
            // wird nicht als Prüffehler umgedeutet.
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
         * Die bisherige einzelne Result-Eigenschaft
         * bezeichnet weiterhin ausdrücklich PSBL.
         *
         * Die neue Results-Eigenschaft enthält
         * alle registrierten Anbieter.
         */
        var psblResult = results["psbl"];

        var previousSnapshot = GetSnapshot();

        /*
         * Der bestehende Zeitstempel bezeichnet
         * weiterhin die letzte erfolgreiche
         * PSBL-Prüfung.
         *
         * Individuelle Prüfzeiten weiterer Anbieter
         * befinden sich später in Results.
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

        /*
         * Alle Ergebnisse gemeinsam veröffentlichen.
         *
         * API-Leser erhalten dadurch entweder
         * den alten oder den neuen vollständigen
         * Monitoring-Zustand.
         */
        Volatile.Write(
            ref _snapshot,
            newSnapshot);

        _logger.LogInformation(
            "Blacklist-Monitoring abgeschlossen. " +
            "Host: {Hostname}, IP: {IpAddress}, " +
            "Anbieter: {ProviderCount}, " +
            "PSBL-Status: {PsblStatus}.",
            _hostname,
            _expectedIpv4,
            results.Count,
            psblResult.Status);
    }

    /// <summary>
    /// Erzeugt für alle registrierten Anbieter
    /// einen technischen Fehlerzustand.
    ///
    /// Beispielsweise dann, wenn bereits
    /// die DNS-Auflösung des Mailservers
    /// fehlgeschlagen ist.
    ///
    /// Kein Anbieter erhält dabei einen
    /// grünen oder roten Listungsstatus.
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
    /// mit eindeutigen Blacklist-Kennungen.
    ///
    /// Wird für Initial- und Fehlerzustände
    /// verwendet.
    ///
    /// Erfolgreiche Batch-Durchläufe liefern
    /// ihre eigene schreibgeschützte Sammlung.
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
                    "Ein Blacklist-Ergebnis besitzt keine gültige Kennung.");
            }

            if (!dictionary.TryAdd(
                    result.BlacklistId,
                    result))
            {
                throw new InvalidOperationException(
                    "Ein Blacklist-Ergebnis wurde mehrfach registriert: " +
                    result.BlacklistId);
            }
        }

        return new ReadOnlyDictionary<
            string,
            BlacklistCheckResult>(dictionary);
    }
}
