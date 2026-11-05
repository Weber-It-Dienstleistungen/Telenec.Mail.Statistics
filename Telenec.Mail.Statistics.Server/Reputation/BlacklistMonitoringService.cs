
using System.Net;
using System.Net.Sockets;

namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Aktueller Zustand der serverseitigen Blacklist-Überwachung.
/// Enthält das letzte Ergebnis und die zugehörigen Zeitpunkte.
/// </summary>
public sealed record BlacklistMonitoringSnapshot(
    string Hostname,
    string ExpectedIpv4,
    IReadOnlyList<string> ResolvedIpv4Addresses,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessfulCheckUtc,
    BlacklistCheckResult Result);

/// <summary>
/// Führt Blacklist-Prüfungen im Hintergrund aus.
///
/// Die Ergebnisse werden ausschließlich im Arbeitsspeicher
/// gehalten. Ein Dashboard-Aufruf löst keine zusätzliche
/// externe DNSBL-Abfrage aus.
///
/// Für den ersten Proof of Work wird PSBL verwendet.
/// Weitere Anbieter können später ergänzt werden.
/// </summary>
public sealed class BlacklistMonitoringService : BackgroundService
{
    private static readonly TimeSpan CheckInterval =
        TimeSpan.FromMinutes(15);

    private readonly TimeProvider _timeProvider;

    private readonly ILogger<BlacklistMonitoringService> _logger;

    private readonly PsblBlacklistChecker _psblChecker = new();

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

        _snapshot = new BlacklistMonitoringSnapshot(
            Hostname: _hostname,
            ExpectedIpv4: _expectedIpv4,
            ResolvedIpv4Addresses: Array.Empty<string>(),
            LastAttemptUtc: null,
            LastSuccessfulCheckUtc: null,
            Result: new BlacklistCheckResult
            {
                BlacklistId = "psbl",
                CheckedTarget = _expectedIpv4,
                Status = BlacklistCheckStatus.NotChecked
            });
    }

    /// <summary>
    /// Liefert den letzten vollständig veröffentlichten
    /// Monitoring-Zustand.
    ///
    /// Diese Methode führt keine DNS-Abfrage aus
    /// und kann daher schnell vom Dashboard
    /// verwendet werden.
    /// </summary>
    public BlacklistMonitoringSnapshot GetSnapshot()
    {
        return Volatile.Read(ref _snapshot);
    }

    /// <summary>
    /// Führt direkt nach dem Start die erste Prüfung aus.
    /// Danach erfolgt eine Prüfung alle 15 Minuten.
    ///
    /// Alle Prüfungen werden nacheinander ausgeführt.
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
    /// Überprüft zuerst die DNS-Auflösung des konfigurierten
    /// Hosts und anschließend die erwartete Versand-IP
    /// gegen PSBL.
    ///
    /// Stimmen DNS-Auflösung und erwartete IP nicht überein,
    /// wird kein sauberer Blacklist-Status erzeugt.
    /// </summary>
    private async Task RunCheckAsync(
        CancellationToken cancellationToken)
    {
        var startedAtUtc = _timeProvider.GetUtcNow();

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
                    result = await _psblChecker.CheckAsync(
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

        var newSnapshot = new BlacklistMonitoringSnapshot(
            Hostname: _hostname,
            ExpectedIpv4: _expectedIpv4,
            ResolvedIpv4Addresses: resolvedAddresses,
            LastAttemptUtc: startedAtUtc,
            LastSuccessfulCheckUtc: lastSuccessfulCheckUtc,
            Result: result);

        // Der gesamte Zustand wird in einem Schritt
        // veröffentlicht. Leser sehen entweder den
        // bisherigen oder den neuen vollständigen Zustand.

        Volatile.Write(ref _snapshot, newSnapshot);

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
    /// Erzeugt einen technischen Fehlerzustand,
    /// beispielsweise bei einer DNS-Abweichung.
    ///
    /// Fehler werden niemals als
    /// "Nicht gelistet" gewertet.
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
            CompletedAtUtc = _timeProvider.GetUtcNow(),
            ErrorMessage = errorMessage
        };
    }
}
