namespace Telenec.Mail.Statistics.Server.MailSecurity;

/// <summary>
/// Sprint 43.4a: reiner, seiteneffektfreier Snapshot-Vergleich.
/// Keine Plesk-Aktionen, keine SQLite-Zugriffe, keine Benachrichtigungen.
///
/// Der Schluessel besteht aus Account + originaler Plesk-Zeitkennung.
/// Diese Zeitkennung wird bewusst NICHT in eine Zeitzone umgerechnet.
/// Der gespeicherte Hoechststand verhindert erneutes Zaehlen beim
/// wiederholten Polling und bei kurzzeitig verschwundenen Zeilen.
/// </summary>
public static class MailSecuritySnapshotChangeDetector
{
    // Begrenzung des kuenftigen Checkpoint-Speichers anhand des
    // letzten tatsaechlichen Beobachtungszeitpunkts (UTC).
    public const int WatermarkRetentionHours = 72;

    public static MailSecuritySnapshotChangeResult Compare(
        MailSecuritySnapshotCheckpoint? previous,
        MailSecurityOverview snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var initial = previous ?? new MailSecuritySnapshotCheckpoint(
            LastSnapshotAtUtc: null,
            Watermarks: Array.Empty<MailSecurityPeriodWatermark>());

        if (!snapshot.Available || snapshot.GeneratedAtUtc is null)
        {
            return new MailSecuritySnapshotChangeResult(
                MailSecuritySnapshotChangeStatus.Unavailable,
                initial,
                Array.Empty<MailSecurityAccountChange>());
        }

        var snapshotAtUtc = snapshot.GeneratedAtUtc.Value.ToUniversalTime();

        // Ein bereits verarbeiteter oder aelterer Snapshot darf
        // niemals einen Checkpoint zuruecksetzen.
        if (initial.LastSnapshotAtUtc is { } previousTimestamp
            && snapshotAtUtc <= previousTimestamp)
        {
            return new MailSecuritySnapshotChangeResult(
                MailSecuritySnapshotChangeStatus.OutOfOrder,
                initial,
                Array.Empty<MailSecurityAccountChange>());
        }

        var firstSnapshot = initial.LastSnapshotAtUtc is null;
        var cutoff = snapshotAtUtc.AddHours(-WatermarkRetentionHours);

        var highWatermarks = new Dictionary<
            (string Account, string Period),
            MailSecurityPeriodWatermark>();

        foreach (var watermark in initial.Watermarks)
        {
            // Zuvor gespeicherte Daten werden nur ueber die
            // autorisierte Historisierung geliefert. Ungueltige
            // Duplikate sollen nicht stillschweigend ueberschreiben.
            var key = Key(watermark.Account, watermark.Period);
            if (!highWatermarks.TryAdd(key, watermark))
            {
                throw new ArgumentException(
                    "Doppelter Stunden-Schluessel im bisherigen Checkpoint.",
                    nameof(previous));
            }
        }

        var changes = new List<MailSecurityAccountChange>();
        var seenKeys = new HashSet<(string Account, string Period)>();

        foreach (var account in snapshot.Accounts)
        {
            long passedIncrease = 0;
            long rejectedIncrease = 0;
            var changingPeriods = 0;

            foreach (var period in account.Periods)
            {
                var key = Key(account.Account, period.Time);
                if (!seenKeys.Add(key))
                {
                    throw new ArgumentException(
                        "Doppelte Account-/Stundenkennung im Snapshot.",
                        nameof(snapshot));
                }

                if (!highWatermarks.TryGetValue(key, out var prior))
                {
                    // Ein neu erschienener Zeitraum ist erst
                    // Ausgangspunkt: seine Werte koennen bereits
                    // vor dem Polling entstanden sein.
                    highWatermarks[key] = new MailSecurityPeriodWatermark(
                        account.Account, period.Time,
                        period.Passed, period.Rejected, snapshotAtUtc);
                    continue;
                }

                var passedDelta = Increase(prior.Passed, period.Passed);
                var rejectedDelta = Increase(prior.Rejected, period.Rejected);

                passedIncrease += passedDelta;
                rejectedIncrease += rejectedDelta;
                if (passedDelta > 0 || rejectedDelta > 0)
                {
                    changingPeriods++;
                }

                highWatermarks[key] = prior with
                {
                    // Kein abgesunkener oder unbekannter Wert darf
                    // den Hoechststand wieder verringern.
                    Passed = MaxKnown(prior.Passed, period.Passed),
                    Rejected = MaxKnown(prior.Rejected, period.Rejected),
                    LastSeenAtUtc = snapshotAtUtc
                };
            }

            // Keine Ausgabe fuer unveraenderte Accounts.
            // Reine Basislinien-/Korrektur-Ereignisse werden
            // weiter unten nur als Vergleichsmetadaten erfasst.
            if (passedIncrease > 0 || rejectedIncrease > 0)
            {
                changes.Add(new MailSecurityAccountChange(
                    account.Account,
                    passedIncrease,
                    rejectedIncrease,
                    changingPeriods,
                    account.OutLimit == 0));
            }
        }

        // Ausgelaufene Watermarks werden nach 72 Stunden ohne
        // Beobachtung entfernt. Ein danach wieder auftauchender
        // Account / Zeitraum wird nur als neue Basislinie
        // aufgenommen, niemals sofort als Zuwachs gewertet.
        var kept = highWatermarks.Values
            .Where(watermark => watermark.LastSeenAtUtc >= cutoff)
            .OrderBy(watermark => watermark.Account,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(watermark => watermark.Period, StringComparer.Ordinal)
            .ToArray();

        var checkpoint = new MailSecuritySnapshotCheckpoint(
            snapshotAtUtc, Array.AsReadOnly(kept));

        var orderedChanges = changes
            .OrderByDescending(change => change.NewRejected)
            .ThenByDescending(change => change.NewPassed)
            .ThenBy(change => change.Account,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var status = firstSnapshot
            ? MailSecuritySnapshotChangeStatus.InitialBaseline
            : orderedChanges.Length > 0
                ? MailSecuritySnapshotChangeStatus.ChangesObserved
                : MailSecuritySnapshotChangeStatus.NoCountGrowth;

        return new MailSecuritySnapshotChangeResult(
            status, checkpoint, Array.AsReadOnly(orderedChanges));
    }

    private static (string Account, string Period) Key(
        string account, string period) =>
        (account.ToLowerInvariant(), period);

    private static long Increase(int? previous, int? current) =>
        previous.HasValue && current.HasValue && current.Value > previous.Value
            ? (long)current.Value - previous.Value
            : 0L;

    private static int? MaxKnown(int? previous, int? current) =>
        previous is null
            ? current
            : current is null
                ? previous
                : Math.Max(previous.Value, current.Value);
}

public enum MailSecuritySnapshotChangeStatus
{
    Unavailable,
    OutOfOrder,
    InitialBaseline,
    NoCountGrowth,
    ChangesObserved
}

/// <summary>
/// Serialisierbarer Checkpoint als Grundlage fuer die spaetere
/// SQLite-Persistenz. Nur Metadaten und Plesk-Zaehler.
/// </summary>
public sealed record MailSecuritySnapshotCheckpoint(
    DateTimeOffset? LastSnapshotAtUtc,
    IReadOnlyList<MailSecurityPeriodWatermark> Watermarks);

public sealed record MailSecurityPeriodWatermark(
    string Account,
    string Period,
    int? Passed,
    int? Rejected,
    DateTimeOffset LastSeenAtUtc);

public sealed record MailSecurityAccountChange(
    string Account,
    long NewPassed,
    long NewRejected,
    int ChangedPeriods,
    bool SendingLimitZero);

public sealed record MailSecuritySnapshotChangeResult(
    MailSecuritySnapshotChangeStatus Status,
    MailSecuritySnapshotCheckpoint Checkpoint,
    IReadOnlyList<MailSecurityAccountChange> Changes);
