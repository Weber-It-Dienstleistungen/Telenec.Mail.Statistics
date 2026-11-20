using System.Text.Json;
using Telenec.Mail.Statistics.Server.MailSecurity;

namespace Telenec.Mail.Statistics.Tests;

/// <summary>
/// Sprint 43.4a: isolierte, frei erfundene Berichtszeilen.
/// Keine Mailbox-Abfragen und keine Speicherung auf dem Server.
/// </summary>
internal static class MailSecuritySnapshotChangeTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    public static Task FirstSnapshotOnlySeedsBaselineAsync()
    {
        var result = Compare(null, 0,
            Account("blocked@example.test", 0, Hour("2026-11-20 08", null, 810)));
        Assert(result.Status == MailSecuritySnapshotChangeStatus.InitialBaseline,
            "Erste Beobachtung darf keine neue Versandaktivitaet behaupten.");
        Assert(result.Changes.Count == 0 && result.Checkpoint.Watermarks.Count == 1,
            "Die 810 historischen Ablehnungen sind nur ein Ausgangsstand.");
        return Task.CompletedTask;
    }

    public static Task RepeatedFiveMinutePollIsNotNewActivityAsync()
    {
        var baseline = Compare(null, 0,
            Account("one@example.test", 150, Hour("2026-11-20 08", 4, 50)));
        var second = Compare(baseline.Checkpoint, 5,
            Account("one@example.test", 150, Hour("2026-11-20 08", 4, 50)));
        Assert(second.Status == MailSecuritySnapshotChangeStatus.NoCountGrowth,
            "Identische Stundenwerte sind keine neue Aktivitaet.");
        Assert(second.Changes.Count == 0,
            "Wiederholte Abfrage muss ohne neue Befunde bleiben.");
        return Task.CompletedTask;
    }

    public static Task RisingHourlyCounterDetectedExactlyOnceAsync()
    {
        var baseline = Compare(null, 0,
            Account("one@example.test", 150, Hour("2026-11-20 08", 100, 150)));
        var second = Compare(baseline.Checkpoint, 5,
            Account("one@example.test", 150, Hour("2026-11-20 08", 101, 180)));
        Assert(second.Status == MailSecuritySnapshotChangeStatus.ChangesObserved,
            "Steigende Werte muessen erkannt werden.");
        var change = Single(second);
        Assert(change.NewRejected == 30 && change.NewPassed == 1
               && change.ChangedPeriods == 1,
            "Nur die tatsaechliche Differenz zum Hoechststand zaehlen.");

        var third = Compare(second.Checkpoint, 10,
            Account("one@example.test", 150, Hour("2026-11-20 08", 101, 180)));
        Assert(third.Changes.Count == 0,
            "Bereits gemessener Zuwachs darf nicht erneut erscheinen.");
        return Task.CompletedTask;
    }

    public static Task NewlyAppearingHourIsOnlyNewBaselineAsync()
    {
        var baseline = Compare(null, 0,
            Account("one@example.test", 150, Hour("2026-11-20 08", 100, 150)));
        var next = Compare(baseline.Checkpoint, 5,
            Account("one@example.test", 150,
                Hour("2026-11-20 08", 100, 150),
                Hour("2026-11-20 09", 12, 40)));
        Assert(next.Changes.Count == 0 && next.Checkpoint.Watermarks.Count == 2,
            "Neu auftauchende Berichtszeilen sind zunaechst historische Basislinien.");
        var after = Compare(next.Checkpoint, 10,
            Account("one@example.test", 150,
                Hour("2026-11-20 08", 100, 150),
                Hour("2026-11-20 09", 12, 55)));
        Assert(Single(after).NewRejected == 15,
            "Erst die spaeteren Zunahmen der neuen Stunde zaehlen.");
        return Task.CompletedTask;
    }

    public static Task RollbackNeverProducesArtificialIncreaseAsync()
    {
        var baseline = Compare(null, 0,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 100)));
        var lower = Compare(baseline.Checkpoint, 5,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 20)));
        Assert(lower.Changes.Count == 0,
            "Ruecklaeufige Zaehler sind keine neue Versandaktivitaet.");

        var partlyRecovered = Compare(lower.Checkpoint, 10,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 95)));
        Assert(partlyRecovered.Changes.Count == 0,
            "Wiederanstieg unter altem Hoechststand darf nicht doppelt zaehlen.");

        var beyondOldHigh = Compare(partlyRecovered.Checkpoint, 15,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 104)));
        Assert(Single(beyondOldHigh).NewRejected == 4,
            "Nur der Anstieg ueber den historischen Hoechststand zaehlt.");
        return Task.CompletedTask;
    }

    public static Task MissingHourThenReturningMustNotDoubleCountAsync()
    {
        var baseline = Compare(null, 0,
            Account("one@example.test", 150, Hour("2026-11-20 08", 5, 100)));
        var missing = Compare(baseline.Checkpoint, 5);
        Assert(missing.Changes.Count == 0
               && missing.Checkpoint.Watermarks.Count == 1,
            "Ein verschwundener Zeitraum muss als Watermark erhalten bleiben.");
        var returned = Compare(missing.Checkpoint, 10,
            Account("one@example.test", 150, Hour("2026-11-20 08", 5, 100)));
        Assert(returned.Changes.Count == 0,
            "Rückkehr einer identischen Stunde darf keinen Zuwachs melden.");
        return Task.CompletedTask;
    }

    public static Task UnknownCounterNeverTreatedAsZeroAsync()
    {
        var baseline = Compare(null, 0,
            Account("one@example.test", 150, Hour("2026-11-20 08", 50, null)));
        var known = Compare(baseline.Checkpoint, 5,
            Account("one@example.test", 150, Hour("2026-11-20 08", null, 200)));
        Assert(known.Changes.Count == 0,
            "Erstmals bekannter Zaehler ist kein Zuwachs von null.");
        var later = Compare(known.Checkpoint, 10,
            Account("one@example.test", 150, Hour("2026-11-20 08", 55, 213)));
        var change = Single(later);
        Assert(change.NewRejected == 13 && change.NewPassed == 5,
            "Fehlende Werte getrennt behandeln und spaetere Zunahmen erkennen.");
        return Task.CompletedTask;
    }

    public static Task OlderAndEqualTimestampMustNotChangeCheckpointAsync()
    {
        var baseline = Compare(null, 10,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 50)));

        var duplicateTimestamp = Compare(baseline.Checkpoint, 10,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 900)));
        Assert(duplicateTimestamp.Status == MailSecuritySnapshotChangeStatus.OutOfOrder
               && ReferenceEquals(duplicateTimestamp.Checkpoint, baseline.Checkpoint),
            "Bereits verarbeiteter Snapshot darf Zustand nicht veraendern.");

        var older = Compare(baseline.Checkpoint, 5,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 900)));
        Assert(older.Status == MailSecuritySnapshotChangeStatus.OutOfOrder
               && older.Changes.Count == 0,
            "Veraltete Daten duerfen keinen Alarm erzeugen.");
        return Task.CompletedTask;
    }

    public static Task UnavailableNeverClearsWatermarksAsync()
    {
        var baseline = Compare(null, 0,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 50)));
        var invalid = new MailSecurityOverview(
            false, "Keine Daten", null, 0, Array.Empty<MailSecurityAccount>());
        var result = MailSecuritySnapshotChangeDetector.Compare(
            baseline.Checkpoint, invalid);
        Assert(result.Status == MailSecuritySnapshotChangeStatus.Unavailable
               && ReferenceEquals(result.Checkpoint, baseline.Checkpoint)
               && result.Changes.Count == 0,
            "Datenluecken duerfen den Checkpoint nicht loeschen.");
        return Task.CompletedTask;
    }

    public static Task CheckpointSurvivesSerializationAndExpiresAsync()
    {
        var baseline = Compare(null, 0,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 50)));
        var json = JsonSerializer.Serialize(baseline.Checkpoint);
        var restored = JsonSerializer.Deserialize<MailSecuritySnapshotCheckpoint>(json);
        Assert(restored is not null && restored.Watermarks.Count == 1,
            "Checkpoint muss fuer spaetere SQLite-Persistenz serialisierbar sein.");

        var next = Compare(restored, 5,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 60)));
        Assert(Single(next).NewRejected == 10,
            "Nach einem Prozessneustart muss der Hoechststand funktionieren.");

        var expired = Compare(next.Checkpoint, 73 * 60);
        Assert(expired.Checkpoint.Watermarks.Count == 0,
            "Abgelaufene Watermarks muessen begrenzt aufbewahrt werden.");

        var fresh = Compare(expired.Checkpoint, 73 * 60 + 5,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 100)));
        Assert(fresh.Changes.Count == 0,
            "Nach Ablauf der Historie erst neue Basislinie aufbauen.");
        return Task.CompletedTask;
    }

    public static Task DirectReturnAfterRetentionIsOnlyBaselineAsync()
    {
        var baseline = Compare(null, 0,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 100)));

        // 73 Stunden spaeter taucht genau dieselbe Plesk-Zeitkennung
        // ohne Zwischensnapshots wieder auf. Alte Basislinie ist
        // abgelaufen und darf keinen kuenstlichen Delta-Alarm geben.
        var returnAfterGap = Compare(baseline.Checkpoint, 73 * 60,
            Account("one@example.test", 150, Hour("2026-11-20 08", 10, 900)));

        Assert(returnAfterGap.Status == MailSecuritySnapshotChangeStatus.NoCountGrowth,
            "Nach langer Datenluecke ist eine neue Basislinie erforderlich.");
        Assert(returnAfterGap.Changes.Count == 0,
            "Abgelaufene Watermark darf keinen scheinbaren Delta-Wert erzeugen.");
        Assert(returnAfterGap.Checkpoint.Watermarks.Count == 1
               && returnAfterGap.Checkpoint.Watermarks[0].Rejected == 900,
            "Neuer Hoechststand muss ohne historische Zuwachsmeldung gespeichert werden.");
        return Task.CompletedTask;
    }

    public static Task CaseInsensitiveAccountAndLimitZeroAsync()
    {
        var baseline = Compare(null, 0,
            Account("ONE@example.test", 150, Hour("2026-11-20 08", 5, 50)));
        var next = Compare(baseline.Checkpoint, 5,
            Account("one@example.test", 0, Hour("2026-11-20 08", 5, 55)));
        var change = Single(next);
        Assert(change.NewRejected == 5 && change.SendingLimitZero,
            "Account-Schreibweise und Limit-0-Kennzeichnung muessen stimmen.");
        return Task.CompletedTask;
    }

    private static MailSecuritySnapshotChangeResult Compare(
        MailSecuritySnapshotCheckpoint? previous,
        int minutesAfter,
        params MailSecurityAccount[] accounts) =>
        MailSecuritySnapshotChangeDetector.Compare(previous, new MailSecurityOverview(
            true, "Test", BaseTime.AddMinutes(minutesAfter),
            Math.Max(1, accounts.Length), accounts));

    private static MailSecurityAccount Account(
        string name, int limit, params MailSecurityPeriod[] periods) =>
        new(name, limit,
            periods.All(p => p.Passed.HasValue)
                ? periods.Sum(p => p.Passed!.Value) : null,
            periods.All(p => p.Rejected.HasValue)
                ? periods.Sum(p => p.Rejected!.Value) : null,
            periods);

    private static MailSecurityPeriod Hour(
        string time, int? passed, int? rejected) =>
        new(time, passed, rejected);

    private static MailSecurityAccountChange Single(
        MailSecuritySnapshotChangeResult result)
    {
        Assert(result.Changes.Count == 1,
            "Genau ein aktualisierter Account erwartet.");
        return result.Changes[0];
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
