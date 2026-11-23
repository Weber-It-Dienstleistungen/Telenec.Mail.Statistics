using Microsoft.Extensions.DependencyInjection;
using Telenec.Mail.Statistics.Server.Data;

namespace Telenec.Mail.Statistics.Server.MailSecurity;

/// <summary>
/// Sprint 43.4c (Schritt 1): Serielle, ausschliesslich beobachtende
/// Verarbeitung bereits validierter Plesk-Mail-Security-Snapshots.
///
/// WICHTIG: Diese Klasse wird bewusst noch NICHT in Program.cs als
/// Hosted Service registriert. Damit aktiviert ihre Aufnahme in
/// das Projekt keinen neuen produktiven Hintergrundprozess.
/// </summary>
public sealed class MailSecurityHistoryWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MailSecuritySnapshotReader _snapshotReader;
    private readonly ILogger<MailSecurityHistoryWorker> _logger;

    public MailSecurityHistoryWorker(
        IServiceScopeFactory scopeFactory,
        MailSecuritySnapshotReader snapshotReader,
        ILogger<MailSecurityHistoryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _snapshotReader = snapshotReader;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        // Der Worker fuehrt niemals parallele Durchlaeufe aus.
        // Jeder Poll ist vollstaendig beendet, bevor der naechste beginnt.
        await Task.Yield();

        await ProcessSnapshotOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ProcessSnapshotOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normales Beenden des Serverdienstes.
        }
    }

    private async Task ProcessSnapshotOnceAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            // Nur den bestehenden, validierenden Reader verwenden.
            // Er liest eine lokale Snapshot-Datei und greift nicht
            // schreibend auf Plesk oder Kundenpostfaecher zu.
            var snapshot = await _snapshotReader.ReadAsync(
                cancellationToken);

            if (!snapshot.Available || snapshot.GeneratedAtUtc is null)
            {
                // Fehlende/veraltete Daten duerfen keine Entwarnung
                // und keine Datenbankveraenderung erzeugen.
                _logger.LogDebug(
                    "Mail-Security-Historie: kein gueltiger Snapshot vorhanden.");
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider
                .GetRequiredService<StatisticsDbContext>();

            // ProcessAsync speichert Checkpoint und Vorfallhistorie
            // gemeinsam in EINER SQLite-Transaktion. Wiederholte
            // Snapshots sind durch die Checkpoint-Logik idempotent.
            var result = await new MailSecurityHistoryStore(database)
                .ProcessAsync(snapshot, cancellationToken);

            if (result.CreatedIncidents > 0
                || result.UpdatedIncidents > 0
                || result.ClosedIncidents > 0)
            {
                // Bewusst keine Accountkennungen im Worker-Protokoll.
                _logger.LogInformation(
                    "Mail-Security-Historie: {Created} neu, {Updated} aktualisiert, "
                    + "{Closed} geschlossen, {Rejected} neu beobachtete Ablehnungen.",
                    result.CreatedIncidents,
                    result.UpdatedIncidents,
                    result.ClosedIncidents,
                    result.NewRejected);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Den regulären Host-Shutdown nicht als Fehler protokollieren.
        }
        catch (Exception exception)
        {
            // Ein fehlerhafter Poll darf weder den Statistikserver
            // beenden noch den naechsten regulären Poll verhindern.
            // Der Store rollt fehlgeschlagene Schreibtransaktionen zurueck.
            _logger.LogError(
                exception,
                "Mail-Security-Historie: Snapshot-Verarbeitung fehlgeschlagen.");
        }
    }
}
