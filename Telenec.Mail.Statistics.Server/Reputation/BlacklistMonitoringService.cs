
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;

namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Aktueller Zustand der serverseitigen
/// Blacklist-Überwachung.
///
/// Die bisherigen Eigenschaften bleiben
/// vollständig erhalten.
///
/// Results ergänzt den Snapshot um eine
/// Sammlung einzelner Prüfergebnisse.
///
/// Result bleibt für die bisherige
/// PSBL-API rückwärtskompatibel bestehen.
/// </summary>
public sealed record BlacklistMonitoringSnapshot(
    string Hostname,
    string ExpectedIpv4,
    IReadOnlyList<string> ResolvedIpv4Addresses,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessfulCheckUtc,
    BlacklistCheckResult Result)
{
    /// <summary>
    /// Prüfergebnisse nach Blacklist-Kennung.
    ///
    /// Die Sammlung wird beim Veröffentlichen
    /// eines Snapshots vollständig erstellt.
    ///
    /// Nach der Veröffentlichung darf sie
    /// nicht mehr verändert werden.
    ///
    /// Derzeit enthält sie nur PSBL.
    /// </summary>
    public IReadOnlyDictionary<string, BlacklistCheckResult>
        Results
    { get; init; } =
            new ReadOnlyDictionary<string, BlacklistCheckResult>(
                new Dictionary<string, BlacklistCheckResult>(
                    StringComparer.Ordinal));
}

/// <summary>
/// Führt Blacklist-Prüfungen im Hintergrund aus.
///
/// Die Ergebnisse bleiben zunächst ausschließlich
/// im Arbeitsspeicher.
///
/// Ein Dashboard-Aufruf löst keine zusätzliche
/// externe Blacklist-Abfrage aus.
///
/// Die Überwachung kann später um weitere
/// Anbieter erweitert werden.
/// </summary>
public sealed class BlacklistMonitoringService : BackgroundService
{
    private static readonly TimeSpan CheckInterval =
        TimeSpan.FromMinutes(15);

    private readonly TimeProvider _timeProvider;

    private readonly ILogger<BlacklistMonitoringService>
        _logger;

    private readonly PsblBlacklistChecker _psblChecker =
        new();

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
         * Initialzustand der PSBL-Prüfung.
         *
         * Der Eintrag gilt technisch als
         * integriert, besitzt aber noch kein
         * abgeschlossenes Prüfergebnis.
         */

        var initialResult = new BlacklistCheckResult
        {
            BlacklistId = "psbl",
            CheckedTarget = _expectedIpv4,
            Status = BlacklistCheckStatus.NotChecked
        };

        _snapshot = new BlacklistMonitoringSnapshot(
            Hostname: _hostname,
            ExpectedIpv4: _expectedIpv4,
            ResolvedIpv4Addresses: Array.Empty<string>(),
            LastAttemptUtc: null,
            LastSuccessfulCheckUtc: null,
            Result: initialResult)
        {
            Results = CreateResultCollection(
                new[] { initialResult })
        };
    }

    /// <summary>
    /// Liefert den zuletzt vollständig
    /// veröffentlichten Monitoring-Snapshot.
    ///
    /// Es wird keine externe Abfrage ausgelöst.
    /// </summary>
    public BlacklistMonitoringSnapshot GetSnapshot()
    {
        return Volatile.Read(ref _snapshot);
    }

    /// <summary>
    /// Führt nach dem Start eine Prüfung aus.
    ///
    /// Weitere Prüfungen erfolgen im
    /// Abstand von 15 Minuten.
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
            // Reguläres Beenden des Dienstes.
        }
    }

    /// <summary>
    /// Prüft zunächst die DNS-Auflösung
    /// des konfigurierten Mailservers.
    ///
    /// Anschließend wird die Versand-IP
    /// gegen PSBL geprüft.
    ///
    /// Spätere Anbieter können zusätzliche
    /// Ergebnisse zur Sammlung beitragen.
    /// </summary>
    private async Task RunCheckAsync(
        CancellationToken cancellationToken)
    {
        var startedAtUtc =
            _timeProvider.GetUtcNow();

        IReadOnlyList<string> resolvedAddresses =
            Array.Empty<string>();

        BlacklistCheckResult result;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IPAddress.TryParse(
                    _expectedIpv4,
                    out var expectedAddress)
                || expectedAddress.AddressFamily
                    != AddressFamily.InterNetwork)
            {
                result = CreateFailure(
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
                    result = CreateFailure(
                        startedAtUtc,
                        "Die erwartete Versand-IP wurde bei der " +
                        "DNS-Auflösung des konfigurierten Hosts " +
                        "nicht gefunden.");
                }
                else
                {
                    result =
                        await _psblChecker.CheckAsync(
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

            result = CreateFailure(
                startedAtUtc,
                "Die DNS-Auflösung oder Blacklist-Prüfung " +
                "ist fehlgeschlagen: " +
                exception.Message);
        }

        var previousSnapshot = GetSnapshot();

        var lastSuccessfulCheckUtc =
            result.IsConclusive
                ? result.CompletedAtUtc
                    ?? _timeProvider.GetUtcNow()
                : previousSnapshot.LastSuccessfulCheckUtc;

        /*
         * Die Ergebnissammlung wird vor
         * der Veröffentlichung erstellt.
         *
         * Aktuell steht genau ein Ergebnis
         * zur Verfügung: PSBL.
         *
         * Später können weitere Ergebnisse
         * ergänzt werden, ohne den Snapshot
         * erneut strukturell ändern zu müssen.
         */

        var results = CreateResultCollection(
            new[] { result });

        var newSnapshot = new BlacklistMonitoringSnapshot(
            Hostname: _hostname,
            ExpectedIpv4: _expectedIpv4,
            ResolvedIpv4Addresses: resolvedAddresses,
            LastAttemptUtc: startedAtUtc,
            LastSuccessfulCheckUtc: lastSuccessfulCheckUtc,
            Result: result)
        {
            Results = results
        };

        /*
         * Der gesamte Monitoring-Zustand
         * wird atomar veröffentlicht.
         *
         * Leser sehen entweder den vorherigen
         * oder den neuen vollständigen Snapshot.
         */

        Volatile.Write(
            ref _snapshot,
            newSnapshot);

        _logger.LogInformation(
            "Blacklist-Prüfung abgeschlossen. " +
            "Host: {Hostname}, IP: {IpAddress}, " +
            "Liste: {BlacklistId}, Status: {Status}.",
            _hostname,
            _expectedIpv4,
            result.BlacklistId,
            result.Status);
    }

    /// <summary>
    /// Erstellt eine schreibgeschützte Sammlung
    /// der bereitgestellten Prüfergebnisse.
    ///
    /// Jede Blacklist erhält genau einen
    /// Eintrag unter ihrer Katalogkennung.
    ///
    /// Doppelte Kennungen werden nicht
    /// stillschweigend überschrieben.
    ///
    /// Die Sammlung wird nach ihrer Erstellung
    /// nicht mehr verändert.
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

    /// <summary>
    /// Erzeugt einen technischen Fehlerzustand.
    ///
    /// Fehler dürfen niemals als
    /// "Nicht gelistet" bewertet werden.
    /// </summary>
    private BlacklistCheckResult CreateFailure(
        DateTimeOffset startedAtUtc,
        string errorMessage)
    {
        return new BlacklistCheckResult
        {
            BlacklistId = "psbl",
            CheckedTarget = _expectedIpv4,
            Status = BlacklistCheckStatus.Failed,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc =
                _timeProvider.GetUtcNow(),
            ErrorMessage = errorMessage
        };
    }
}
