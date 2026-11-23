using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Telenec.Mail.Statistics.Server.Data;

namespace Telenec.Mail.Statistics.Server.MailSecurity;

/// <summary>
/// Sprint 43.4b: bewusst NICHT im Produktiv-DI oder in einem Hosted
/// Service registriert. Erst nach Regressionstests wird entschieden,
/// wann und wie neue Snapshots periodisch verarbeitet werden.
///
/// Ein gueltiger Snapshot wird exakt einmal verarbeitet:
/// Checkpoint, aggregierte neue Zaehler, Vorfallabschluss und
/// Aufbewahrungsbereinigung werden transaktional gespeichert.
/// </summary>
public sealed class MailSecurityHistoryStore
{
    public const int IncidentRetentionDays = 30;
    public const int IdleClosureHours = 24;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly StatisticsDbContext _database;

    public MailSecurityHistoryStore(StatisticsDbContext database)
    {
        _database = database;
    }

    public async Task<MailSecurityHistoryWriteResult> ProcessAsync(
        MailSecurityOverview snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Available || snapshot.GeneratedAtUtc is null)
        {
            return new(
                MailSecuritySnapshotChangeStatus.Unavailable,
                0, 0, 0, 0);
        }

        // Verwenden wir zukuenftig mehr als einen Worker, muessen
        // Schreibzugriffe zusaetzlich pro SQLite-Datenbank serialisiert
        // werden (BEGIN IMMEDIATE). Ein offener Vorfall je Konto wird
        // bereits durch einen partiellen UNIQUE-Index erzwungen.
        await using var transaction = await _database.Database
            .BeginTransactionAsync(cancellationToken);

        var state = await _database.MailSecurityCheckpointStates
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);

        MailSecuritySnapshotCheckpoint? prior = null;
        if (state is not null)
        {
            prior = JsonSerializer.Deserialize<MailSecuritySnapshotCheckpoint>(
                state.CheckpointJson, JsonOptions)
                ?? throw new InvalidDataException(
                    "Gespeicherter Mail-Security-Checkpoint ist ungueltig.");
        }

        var comparison = MailSecuritySnapshotChangeDetector.Compare(
            prior, snapshot);

        if (comparison.Status is MailSecuritySnapshotChangeStatus.OutOfOrder
            or MailSecuritySnapshotChangeStatus.Unavailable)
        {
            // Auch ohne explizites Commit wird nichts geschrieben.
            return new(comparison.Status, 0, 0, 0, 0);
        }

        var observedAtUtc =
            comparison.Checkpoint.LastSnapshotAtUtc!.Value.UtcDateTime;

        // Alle aktiven Beobachtungen holen wir vor der Aenderung.
        // Es ist maximal ein offener Datensatz je Account erlaubt.
        var openIncidents = await _database.MailSecurityIncidents
            .Where(incident => incident.ClosedAtUtc == null)
            .ToListAsync(cancellationToken);

        var closedCount = 0;
        var idleCutoffUtc = observedAtUtc.AddHours(-IdleClosureHours);

        foreach (var incident in openIncidents)
        {
            if (incident.LastObservedUtc < idleCutoffUtc)
            {
                incident.ClosedAtUtc = observedAtUtc;
                closedCount++;
            }
        }

        var activeByAccount = openIncidents
            .Where(incident => incident.ClosedAtUtc is null)
            .ToDictionary(
                incident => incident.Account,
                StringComparer.OrdinalIgnoreCase);

        // Die aktuelle Risikoauswertung liefert nur Kontext / eine
        // maximale Beobachtungsprioritaet, kein Spam-Urteil.
        // Ein nicht klassifizierter Zuwachs bleibt intern auf "None".
        var riskByAccount = MailSecurityRiskEvaluator
            .Evaluate(snapshot)
            .Findings
            .ToDictionary(finding => finding.Account,
                finding => finding.Level,
                StringComparer.OrdinalIgnoreCase);

        var createdCount = 0;
        var updatedCount = 0;
        var newRejected = 0L;
        var newPassed = 0L;

        foreach (var delta in comparison.Changes)
        {
            // Nur neue Ablehnungen begruenden eine zeitliche
            // Sicherheitsbeobachtung. Reine Passed-Aenderungen
            // erzeugen keine Vorfaelle.
            if (delta.NewRejected <= 0)
            {
                continue;
            }

            var account = delta.Account.ToLowerInvariant();
            var level = riskByAccount.TryGetValue(account, out var risk)
                ? risk
                : MailSecurityRiskLevel.None;

            newRejected = checked(newRejected + delta.NewRejected);
            newPassed = checked(newPassed + delta.NewPassed);

            if (activeByAccount.TryGetValue(account, out var incident))
            {
                incident.LastObservedUtc = observedAtUtc;
                incident.NewRejected = checked(
                    incident.NewRejected + delta.NewRejected);
                incident.NewPassed = checked(
                    incident.NewPassed + delta.NewPassed);
                incident.MaxRiskLevel = (MailSecurityRiskLevel)Math.Max(
                    (int)incident.MaxRiskLevel, (int)level);
                incident.SendingLimitZero = delta.SendingLimitZero;
                updatedCount++;
            }
            else
            {
                incident = new MailSecurityIncident
                {
                    Id = Guid.NewGuid(),
                    Account = account,
                    FirstObservedUtc = observedAtUtc,
                    LastObservedUtc = observedAtUtc,
                    NewRejected = delta.NewRejected,
                    NewPassed = delta.NewPassed,
                    MaxRiskLevel = level,
                    SendingLimitZero = delta.SendingLimitZero
                };
                _database.MailSecurityIncidents.Add(incident);
                activeByAccount.Add(account, incident);
                createdCount++;
            }
        }

        // Aufbewahrung: nur abgeschlossene Faelle, 30 Tage nach
        // Schliessung. Aktive Faelle bleiben fuer die Untersuchung.
        // Der automatische Cleanup im Leerlauf ist ein spaeterer
        // Hintergrunddienst; hier gilt nur der gueltige Snapshot.
        var retentionCutoffUtc =
            observedAtUtc.AddDays(-IncidentRetentionDays);

        await _database.MailSecurityIncidents
            .Where(incident => incident.ClosedAtUtc != null
                && incident.ClosedAtUtc < retentionCutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);

        var checkpointJson = JsonSerializer.Serialize(
            comparison.Checkpoint, JsonOptions);

        if (state is null)
        {
            _database.MailSecurityCheckpointStates.Add(
                new MailSecurityCheckpointState
                {
                    Id = 1,
                    CheckpointJson = checkpointJson,
                    UpdatedAtUtc = observedAtUtc
                });
        }
        else
        {
            state.CheckpointJson = checkpointJson;
            state.UpdatedAtUtc = observedAtUtc;
        }

        await _database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new(comparison.Status, createdCount, updatedCount,
            closedCount, newRejected);
    }

    public async Task<MailSecurityHistorySnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await _database.MailSecurityCheckpointStates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);

        var incidents = await _database.MailSecurityIncidents
            .AsNoTracking()
            .OrderByDescending(x => x.LastObservedUtc)
            .ToListAsync(cancellationToken);

        return new(
            state?.UpdatedAtUtc,
            incidents.Select(incident => new MailSecurityHistoryItem(
                incident.Id, incident.Account, incident.FirstObservedUtc,
                incident.LastObservedUtc, incident.ClosedAtUtc,
                incident.NewRejected, incident.NewPassed,
                incident.MaxRiskLevel, incident.SendingLimitZero))
                .ToArray());
    }
}

public sealed record MailSecurityHistoryWriteResult(
    MailSecuritySnapshotChangeStatus Status,
    int CreatedIncidents,
    int UpdatedIncidents,
    int ClosedIncidents,
    long NewRejected);

public sealed record MailSecurityHistoryItem(
    Guid Id,
    string Account,
    DateTime FirstObservedUtc,
    DateTime LastObservedUtc,
    DateTime? ClosedAtUtc,
    long NewRejected,
    long NewPassed,
    MailSecurityRiskLevel MaxRiskLevel,
    bool SendingLimitZero);

public sealed record MailSecurityHistorySnapshot(
    DateTime? LastProcessedUtc,
    IReadOnlyList<MailSecurityHistoryItem> Incidents);
