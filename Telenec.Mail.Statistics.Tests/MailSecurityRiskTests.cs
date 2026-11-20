using System.Text.Json;
using Telenec.Mail.Statistics.Server.MailSecurity;

namespace Telenec.Mail.Statistics.Tests;

/// <summary>
/// Sprint 43.1: isolierte Beispieldaten, keine echten Kundenkonten.
/// Keine Plesk-Verbindung, keine Mails, keine Postfach-Aktionen.
/// </summary>
internal static class MailSecurityRiskTests
{
    public static Task StrongRepeatedRejectionsAsync()
    {
        var result = Classify(Account(
            "critical@example.test", 150,
            Period("2026-11-20 05", 149, 285),
            Period("2026-11-20 06", 150, 245),
            Period("2026-11-20 07", 33, 113)));

        Assert(result.Level == MailSecurityRiskLevel.Critical,
            "Wiederholte 643 Ablehnungen mit hohem Anteil muessen kritisch sein.");
        Assert(result.Rejected == 643 && result.Passed == 332,
            "Ausgangswerte duerfen nicht verfremdet werden.");
        Assert(result.RejectedPeriods == 3,
            "Stundenzeilen duerfen nicht vermischt werden.");
        return Task.CompletedTask;
    }

    public static Task ElevatedRejectionsAsync()
    {
        var result = Classify(Account(
            "warning@example.test", 150,
            Period("2026-11-20 05", 149, 50),
            Period("2026-11-20 06", 150, 35),
            Period("2026-11-20 07", 19, 18)));

        Assert(result.Level == MailSecurityRiskLevel.Warning,
            "103 Ablehnungen bei 318 Annahmen muessen als Warnung markiert werden.");
        return Task.CompletedTask;
    }

    public static Task NormalMailTrafficAsync()
    {
        var report = MailSecurityRiskEvaluator.Evaluate(
            Snapshot(Account(
                "legitimate@example.test", 150,
                Period("2026-11-20 07", 120, 3))));

        Assert(report.Available && report.EvaluatedAccounts == 1,
            "Gueltiger Snapshot muss ausgewertet werden.");
        Assert(report.Findings.Count == 0,
            "Wenige Ablehnungen duerfen keinen Fehlalarm erzeugen.");
        return Task.CompletedTask;
    }

    public static Task HighVolumeLowRatioAsync()
    {
        var report = MailSecurityRiskEvaluator.Evaluate(
            Snapshot(Account(
                "bulk@example.test", 10000,
                Period("2026-11-20 07", 10000, 150))));

        Assert(report.Findings.Count == 0,
            "Hohe absolute Versandmenge allein beweist keinen Missbrauch.");
        return Task.CompletedTask;
    }

    public static Task BlockedAccountIsStillHistoricalAsync()
    {
        var finding = Classify(Account(
            "blocked@example.test", 0,
            Period("2026-11-20 05", 149, 285),
            Period("2026-11-20 06", 150, 245),
            Period("2026-11-20 07", 33, 113)));

        Assert(finding.SendingLimitZero,
            "Versandlimit 0 muss gesondert gekennzeichnet sein.");
        Assert(finding.Level == MailSecurityRiskLevel.Critical,
            "Historische Risikoindikatoren duerfen nicht verschwinden.");
        // Keine Aussage zu einem aktuellen Angriff oder neuen Versuchen.
        return Task.CompletedTask;
    }

    public static Task MissingPassedIsNotZeroAsync()
    {
        var finding = Classify(new MailSecurityAccount(
            "unknown-pass@example.test", 150, null, 120,
            new[] { Period("2026-11-20 07", null, 120) }));

        Assert(finding.Passed is null,
            "Fehlende Passed-Werte sind nicht gleich null Nachrichten.");
        Assert(finding.RejectionSharePercent is null,
            "Bei unbekanntem Nenner darf keine Quote berechnet werden.");
        Assert(finding.Level == MailSecurityRiskLevel.Warning,
            "Hohe Ablehnungen bei unsicherer Datenlage bleiben Warnungen.");
        Assert(finding.Reason == MailSecurityRiskReason.HighRejectionsWithoutPassed,
            "Der Grund muss fehlende Annahmedaten erkennen lassen.");
        return Task.CompletedTask;
    }

    public static Task PartialPeriodsHaveNoReliableRatioAsync()
    {
        // Plesk summiert bekannte Passed-Werte auch dann, wenn
        // andere Zeitzeilen keinen Passed-Wert enthalten.
        // Daraus darf keine scheinbar praezise Quote entstehen.
        var account = new MailSecurityAccount(
            "partial@example.test", 150, 40, 120,
            new[]
            {
                Period("2026-11-20 06", 40, 20),
                Period("2026-11-20 07", null, 100)
            });

        var finding = Classify(account);
        Assert(finding.Level == MailSecurityRiskLevel.Warning,
            "Bei unvollstaendigen Zeilen nur Warnung statt Quoten-Eskalation.");
        Assert(finding.RejectionSharePercent is null,
            "Unvollstaendige Stundenwerte erlauben keine sichere Quote.");
        Assert(finding.Reason == MailSecurityRiskReason.HighRejectionsWithoutPassed,
            "Die Unsicherheit muss als eigene Ursache erkennbar sein.");
        return Task.CompletedTask;
    }

    public static Task MissingRejectedIsNotZeroAsync()
    {
        var report = MailSecurityRiskEvaluator.Evaluate(
            Snapshot(new MailSecurityAccount(
                "unknown-reject@example.test", 150, 250, null,
                new[] { Period("2026-11-20 07", 250, null) })));

        Assert(report.Findings.Count == 0,
            "Fehlende Rejected-Werte duerfen nicht als Zahl behandelt werden.");
        return Task.CompletedTask;
    }

    public static Task SingleHourIsNotRepeatedEvidenceAsync()
    {
        var finding = Classify(Account(
            "one-hour@example.test", 150,
            Period("2026-11-20 07", 100, 300)));

        Assert(finding.Level == MailSecurityRiskLevel.Warning,
            "Eine einmalige Stunde unter der Extremgrenze ist keine wiederholte Beobachtung.");
        return Task.CompletedTask;
    }

    public static Task ExtremeSingleHourIsCriticalAsync()
    {
        var finding = Classify(Account(
            "extreme@example.test", 150,
            Period("2026-11-20 07", 200, 600)));

        Assert(finding.Level == MailSecurityRiskLevel.Critical,
            "Extrem hohe Einzelperioden muessen erkennbar bleiben.");
        Assert(finding.Reason == MailSecurityRiskReason.ExtremeSinglePeriod,
            "Eine einzelne Stunde darf nicht als wiederholt bezeichnet werden.");
        return Task.CompletedTask;
    }

    public static Task LowLevelObservationAsync()
    {
        var finding = Classify(Account(
            "observe@example.test", 150,
            Period("2026-11-20 07", 25, 25)));

        Assert(finding.Level == MailSecurityRiskLevel.Observe,
            "Kleinere Auffaelligkeiten sind Beobachtung, nicht Warnung.");
        return Task.CompletedTask;
    }

    public static Task RepeatedPollIsIdempotentAsync()
    {
        var snapshot = Snapshot(Account(
            "repeat@example.test", 150,
            Period("2026-11-20 05", 150, 245),
            Period("2026-11-20 06", 150, 285)));

        var first = MailSecurityRiskEvaluator.Evaluate(snapshot);
        var second = MailSecurityRiskEvaluator.Evaluate(snapshot);

        Assert(first.Findings.Count == 1 && second.Findings.Count == 1,
            "Wiederholte Snapshot-Abfrage erzeugt nicht mehrere Faelle.");
        Assert(first.Findings[0] == second.Findings[0],
            "Dieselbe Datenlage muss identisch bewertet werden.");
        Assert(first.Findings[0].Rejected == 530,
            "Stundenwerte duerfen nicht ueber Polls aufsummiert werden.");
        return Task.CompletedTask;
    }

    public static Task UnavailableSnapshotIsNotNormalTrafficAsync()
    {
        var snapshot = new MailSecurityOverview(
            false, "Veraltet", null, 0, Array.Empty<MailSecurityAccount>());

        var report = MailSecurityRiskEvaluator.Evaluate(snapshot);
        Assert(!report.Available && report.Findings.Count == 0,
            "Fehlende Daten sind keine positive Sicherheitsbewertung.");
        return Task.CompletedTask;
    }

    public static Task DeterministicPriorityOrderingAsync()
    {
        var report = MailSecurityRiskEvaluator.Evaluate(
            Snapshot(
                Account("normal@example.test", 150,
                    Period("2026-11-20 07", 10, 2)),
                Account("warn@example.test", 150,
                    Period("2026-11-20 07", 318, 103)),
                Account("critical@example.test", 150,
                    Period("2026-11-20 05", 150, 200),
                    Period("2026-11-20 06", 150, 250))));

        Assert(report.Findings.Count == 2,
            "Nur auffaellige Konten gehoeren in die Ergebnisliste.");
        Assert(report.Findings[0].Level == MailSecurityRiskLevel.Critical
               && report.Findings[1].Level == MailSecurityRiskLevel.Warning,
            "Kritische Auffaelligkeiten muessen zuerst erscheinen.");
        return Task.CompletedTask;
    }

    public static Task ApiEnumsSerializeAsNamesAsync()
    {
        var report = MailSecurityRiskEvaluator.Evaluate(
            Snapshot(Account(
                "sample@example.test", 150,
                Period("2026-11-20 05", 150, 245),
                Period("2026-11-20 06", 150, 285))));

        var json = JsonSerializer.Serialize(
            report, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert(json.Contains("\\"level\\":\\"Critical\\"", StringComparison.Ordinal),
            "API muss eindeutige, lesbare Risikostufen ausgeben.");
        Assert(json.Contains("\\"reason\\":\\"HighRepeatedRejections\\"", StringComparison.Ordinal),
            "API muss Ursachen als stabil lesbare Kennungen liefern.");
        Assert(!json.Contains("sample@example.test", StringComparison.OrdinalIgnoreCase)
               == false,
            "Testfall muss personenbezogene Werte nur im geschuetzten Ergebnis enthalten.");

        return Task.CompletedTask;
    }

    private static MailSecurityRiskFinding Classify(
        MailSecurityAccount account)
    {
        var report = MailSecurityRiskEvaluator.Evaluate(Snapshot(account));
        Assert(report.Findings.Count == 1,
            "Genau ein auffaelliger Test-Account erwartet.");
        return report.Findings[0];
    }

    private static MailSecurityOverview Snapshot(
        params MailSecurityAccount[] accounts) =>
        new(true, "Test", new DateTimeOffset(
                2026, 11, 20, 8, 0, 0, TimeSpan.Zero),
            accounts.Length, accounts);

    private static MailSecurityPeriod Period(
        string time, int? passed, int? rejected) =>
        new(time, passed, rejected);

    private static MailSecurityAccount Account(
        string name, int limit, params MailSecurityPeriod[] periods) =>
        new(
            name,
            limit,
            periods.All(p => p.Passed.HasValue)
                ? periods.Sum(p => p.Passed!.Value)
                : null,
            periods.All(p => p.Rejected.HasValue)
                ? periods.Sum(p => p.Rejected!.Value)
                : null,
            periods);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
