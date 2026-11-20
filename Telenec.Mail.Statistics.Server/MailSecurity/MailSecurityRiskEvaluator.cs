namespace Telenec.Mail.Statistics.Server.MailSecurity;

/// <summary>
/// Sprint 43: ausschliesslich lesende, zunaechst nicht produktiv
/// angebundene Klassifizierung aus EINEM gueltigen Plesk-Snapshot.
///
/// WICHTIG:
/// - Plesk-Stundenzeilen sind keine vollstaendige 24h-Historie.
/// - Wiederholte 5-Minuten-Abfragen duerfen nicht addiert werden.
/// - Der angegebene Periodenzeitpunkt wird nicht als verifizierte
///   UTC-Zeit interpretiert.
/// - Ein hoher Wert ist ein Untersuchungsanlass, kein Spam-Nachweis.
/// - Keine Postfachaktion, kein Versand, kein Datenbankzugriff.
/// </summary>
public static class MailSecurityRiskEvaluator
{
    /// <summary>
    /// Absichtlich konservative erste Testschwellen.
    /// Vor produktiven Warnungen anhand realer, autorisiert
    /// ausgewerteter Vorfaelle kalibrieren.
    /// </summary>
    private const int ObservationRejected = 25;
    private const int WarningRejected = 100;
    private const int CriticalRejected = 300;
    private const int SinglePeriodCriticalRejected = 500;

    public static MailSecurityRiskReport Evaluate(
        MailSecurityOverview snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Available || snapshot.GeneratedAtUtc is null)
        {
            return new MailSecurityRiskReport(
                Available: false,
                GeneratedAtUtc: null,
                EvaluatedAccounts: 0,
                Findings: Array.Empty<MailSecurityRiskFinding>());
        }

        var findings = new List<MailSecurityRiskFinding>();

        foreach (var account in snapshot.Accounts)
        {
            if (account is null)
            {
                throw new ArgumentException(
                    "Die Plesk-Daten enthalten ein ungueltiges Postfach.",
                    nameof(snapshot));
            }

            // Die SnapshotReader-Validierung bleibt alleinige
            // Eingangskontrolle. Hier kein stillschweigendes
            // Auffuellen unbekannter passed/rejected-Werte.
            if (account.Rejected is null || account.Rejected < ObservationRejected)
            {
                continue;
            }

            var rejected = account.Rejected.Value;
            var passed = account.Passed;

            // Plesk liefert teils unvollstaendige Zeilen. Die
            // Gesamtsumme Passed kann in diesem Fall trotzdem nur
            // die bekannten Zeilen enthalten. Eine vermeintliche
            // Quote waere daher fachlich nicht belastbar.
            var completePeriodPairs = account.Periods.Count > 0
                && account.Periods.All(period =>
                    period.Passed.HasValue
                    && period.Rejected.HasValue);

            var ratio = passed.HasValue && completePeriodPairs
                ? (double)rejected / ((long)passed.Value + rejected)
                : (double?)null;

            // Hier werden ausschliesslich unterschiedliche
            // Plesk-Berichtszeilen desselben EINEN Snapshots
            // betrachtet, niemals mehrere Snapshot-Abrufe addiert.
            var rejectedPeriods = account.Periods.Count(period =>
                period.Rejected is > 0);

            var highRejectedPeriods = account.Periods.Count(period =>
                period.Rejected is >= 50);

            var level = DetermineLevel(
                rejected, ratio, rejectedPeriods, highRejectedPeriods);

            if (level == MailSecurityRiskLevel.None)
            {
                continue;
            }

            // Der Bestandswert bleibt ein historischer
            // Quellindikator. Auch wenn das aktuelle Versandlimit
            // inzwischen 0 ist, wird daraus weder ein neuer
            // Versandversuch noch ein aktuell aktiver Angriff.
            var reason = level switch
            {
                MailSecurityRiskLevel.Critical =>
                    highRejectedPeriods >= 2
                        ? MailSecurityRiskReason.HighRepeatedRejections
                        : MailSecurityRiskReason.ExtremeSinglePeriod,
                MailSecurityRiskLevel.Warning =>
                    ratio.HasValue
                        ? MailSecurityRiskReason.ElevatedRejectionShare
                        : MailSecurityRiskReason.HighRejectionsWithoutPassed,
                MailSecurityRiskLevel.Observe =>
                    MailSecurityRiskReason.UnusualRejectionVolume,
                _ => throw new InvalidOperationException(
                    "Unbekannte Risikostufe.")
            };

            findings.Add(new MailSecurityRiskFinding(
                Account: account.Account,
                Level: level,
                Reason: reason,
                Passed: passed,
                Rejected: rejected,
                RejectedPeriods: rejectedPeriods,
                SendingLimitZero: account.OutLimit == 0,
                RejectionSharePercent: ratio * 100));
        }

        return new MailSecurityRiskReport(
            Available: true,
            GeneratedAtUtc: snapshot.GeneratedAtUtc,
            EvaluatedAccounts: snapshot.Accounts.Count,
            Findings: findings
                .OrderByDescending(finding => finding.Level)
                .ThenByDescending(finding => finding.Rejected)
                .ThenBy(finding => finding.Account,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static MailSecurityRiskLevel DetermineLevel(
        int rejected,
        double? ratio,
        int rejectedPeriods,
        int highRejectedPeriods)
    {
        // Kritisch nur bei hohem absoluten Volumen
        // UND hohem Anteil UND Verteilung auf mindestens
        // zwei belastbare Stundenzeilen, oder bei einer
        // extrem ausgepraegten Einzelperiode.
        if (ratio is >= 0.40
            && rejected >= CriticalRejected
            && (highRejectedPeriods >= 2
                || rejected >= SinglePeriodCriticalRejected))
        {
            return MailSecurityRiskLevel.Critical;
        }

        // Hohe absolute Ablehnungszahl mit mehr als
        // 20 % der ausgewerteten Annahme-/Ablehnungsdaten.
        // Fehlen die Passed-Daten, markieren wir die
        // Unsicherheit statt den Nenner zu erfinden.
        if (rejected >= WarningRejected
            && (ratio is >= 0.20 || ratio is null))
        {
            return MailSecurityRiskLevel.Warning;
        }

        // Eine Beobachtung ist noch keine Warnung.
        // Die gesamte Datenlage kann unvollstaendig sein.
        if (rejected >= ObservationRejected
            && (ratio is >= 0.10 || ratio is null)
            && rejectedPeriods > 0)
        {
            return MailSecurityRiskLevel.Observe;
        }

        return MailSecurityRiskLevel.None;
    }
}

public enum MailSecurityRiskLevel
{
    None = 0,
    Observe = 1,
    Warning = 2,
    Critical = 3
}

public enum MailSecurityRiskReason
{
    UnusualRejectionVolume,
    ElevatedRejectionShare,
    HighRejectionsWithoutPassed,
    HighRepeatedRejections,
    ExtremeSinglePeriod
}

public sealed record MailSecurityRiskFinding(
    string Account,
    MailSecurityRiskLevel Level,
    MailSecurityRiskReason Reason,
    int? Passed,
    int Rejected,
    int RejectedPeriods,
    bool SendingLimitZero,
    double? RejectionSharePercent);

public sealed record MailSecurityRiskReport(
    bool Available,
    DateTimeOffset? GeneratedAtUtc,
    int EvaluatedAccounts,
    IReadOnlyList<MailSecurityRiskFinding> Findings);
