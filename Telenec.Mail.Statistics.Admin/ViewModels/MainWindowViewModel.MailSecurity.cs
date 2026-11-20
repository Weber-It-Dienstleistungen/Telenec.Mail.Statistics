using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using Telenec.Mail.Statistics.Admin.Models;

namespace Telenec.Mail.Statistics.Admin.ViewModels;

/// <summary>
/// Vierter Reiter: Plesk Outgoing Mail Control.
/// Keine schreibenden Aktionen, keine selbsttaetigen Sperren.
/// Unvollstaendige oder veraltete Daten nie als Nullwerte ausgeben.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private string mailSecurityStatusText =
        "Plesk-Sicherheitsdaten werden geladen …";

    [ObservableProperty]
    private string mailSecurityDataAgeText =
        "Datenstand: nicht verfügbar";

    [ObservableProperty]
    private string mailSecurityMailboxCountText = "—";

    [ObservableProperty]
    private string mailSecurityActiveCountText = "—";

    [ObservableProperty]
    private string mailSecurityRejectedCountText = "—";

    [ObservableProperty]
    private IReadOnlyList<MailSecurityAccountRow> mailSecurityAccounts =
        Array.Empty<MailSecurityAccountRow>();

    [ObservableProperty]
    private MailSecurityAccountRow? selectedMailSecurityAccount;

    // Sprint 43.3: Beobachtende Risikoanalyse, unabhaengig
    // von den bisherigen Plesk-Mail-Sicherheitsstatistiken.
    [ObservableProperty]
    private string mailSecurityRiskStatusText =
        "Risikobewertung wird geladen …";

    [ObservableProperty]
    private IReadOnlyList<MailSecurityRiskRow> mailSecurityRiskFindings =
        Array.Empty<MailSecurityRiskRow>();

    private async Task LoadMailSecurityAsync(
        CancellationToken cancellationToken)
    {
        MailSecurityStatusText =
            "Plesk-Sicherheitsdaten werden geladen …";
        MailSecurityDataAgeText = "Datenstand: nicht verfügbar";
        MailSecurityMailboxCountText = "—";
        MailSecurityActiveCountText = "—";
        MailSecurityRejectedCountText = "—";
        MailSecurityAccounts = Array.Empty<MailSecurityAccountRow>();
        SelectedMailSecurityAccount = null;

        try
        {
            var response =
                await _statisticsClient.GetMailSecurityAsync(cancellationToken);

            if (!response.Available)
            {
                MailSecurityStatusText =
                    string.IsNullOrWhiteSpace(response.StatusMessage)
                        ? "Mail-Sicherheit ist noch nicht eingerichtet."
                        : response.StatusMessage;
                return;
            }

            if (response.Accounts is null
                || response.MailboxCount < response.Accounts.Count
                || response.MailboxCount < 1
                || response.GeneratedAtUtc is null)
            {
                throw new InvalidDataException(
                    "Der Plesk-Sicherheitsbericht ist unvollständig.");
            }

            var rows = response.Accounts
                .Select(account =>
                {
                    if (account is null
                        || string.IsNullOrWhiteSpace(account.Account)
                        || account.OutLimit < 0
                        || account.Passed is < 0
                        || account.Rejected is < 0
                        || account.Periods is null)
                    {
                        throw new InvalidDataException(
                            "Ungültige Mailaccount-Statistiken.");
                    }

                    var periods = account.Periods
                        .Select(period =>
                        {
                            if (period is null
                                || string.IsNullOrWhiteSpace(period.Time))
                            {
                                throw new InvalidDataException(
                                    "Ungültige Plesk-Stundenstatistik.");
                            }

                            return new MailSecurityPeriodRow(
                                Time: period.Time,
                                PassedText:
                                    period.Passed?.ToString("N0") ?? "—",
                                RejectedText:
                                    period.Rejected?.ToString("N0") ?? "—");
                        })
                        .ToArray();

                    return new MailSecurityAccountRow(
                        account.Account,
                        account.OutLimit,
                        account.Passed,
                        account.Rejected,
                        Array.AsReadOnly(periods));
                })
                .OrderByDescending(account => account.Rejected ?? 0)
                .ThenByDescending(account => account.Passed ?? 0)
                .ThenBy(account => account.Account,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

            MailSecurityAccounts = Array.AsReadOnly(rows);
            SelectedMailSecurityAccount = rows.FirstOrDefault();

            MailSecurityMailboxCountText =
                response.MailboxCount.ToString("N0");

            MailSecurityActiveCountText =
                rows.Length.ToString("N0");

            MailSecurityRejectedCountText =
                rows.Count(row => row.Rejected is > 0).ToString("N0");

            MailSecurityDataAgeText =
                "Datenstand: " +
                response.GeneratedAtUtc.Value.ToLocalTime()
                    .ToString("dd.MM.yyyy HH:mm:ss");

            MailSecurityStatusText =
                string.IsNullOrWhiteSpace(response.StatusMessage)
                    ? "Plesk-Lesedaten verfügbar; keine administrative Steuerung aktiv."
                    : response.StatusMessage;
        }
        catch (UnauthorizedAccessException)
        {
            MailSecurityStatusText =
                "Mail-Sicherheit: Der Admin-API-Schlüssel wurde abgelehnt.";
        }
        catch (NotSupportedException)
        {
            MailSecurityStatusText =
                "Der Server bietet die Mail-Sicherheits-API noch nicht an.";
        }
        catch (HttpRequestException)
        {
            MailSecurityStatusText =
                "Mail-Sicherheit konnte den Server nicht erreichen.";
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            MailSecurityStatusText =
                "Zeitüberschreitung bei der Mail-Sicherheitsabfrage.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            MailSecurityStatusText =
                "Plesk-Sicherheitsdaten konnten nicht zuverlässig ausgewertet werden.";
        }
    }

    /// <summary>
    /// Die Risikobewertung ist ein OPTIONALER Endpunkt.
    /// Der produktive Server 0.1.3 liefert hier noch HTTP 404.
    /// Das darf die bisherige Mail-Sicherheitsansicht nicht beeintraechtigen.
    /// </summary>
    private async Task LoadMailSecurityRiskAsync(
        CancellationToken cancellationToken)
    {
        MailSecurityRiskStatusText = "Risikobewertung wird geladen …";
        MailSecurityRiskFindings = Array.Empty<MailSecurityRiskRow>();

        try
        {
            var report =
                await _statisticsClient.GetMailSecurityRiskAsync(cancellationToken);

            if (!report.Available)
            {
                MailSecurityRiskStatusText =
                    "Keine aktuellen Risikodaten verfügbar – keine Entwarnung.";
                return;
            }

            if (report.GeneratedAtUtc is null
                || report.EvaluatedAccounts < 0
                || report.Findings is null
                || report.Findings.Count > report.EvaluatedAccounts)
            {
                throw new InvalidDataException(
                    "Die Risikoauswertung enthält unvollständige Angaben.");
            }

            var reasons = new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["UnusualRejectionVolume"] =
                    "Ungewöhnlich viele Ablehnungen",
                ["ElevatedRejectionShare"] =
                    "Erhöhter Ablehnungsanteil",
                ["HighRejectionsWithoutPassed"] =
                    "Viele Ablehnungen; Annahmedaten unvollständig",
                ["HighRepeatedRejections"] =
                    "Hohe Ablehnungen in mehreren Berichtszeilen",
                ["ExtremeSinglePeriod"] =
                    "Extrem viele Ablehnungen in einer Berichtszeile"
            };

            var levels = new Dictionary<string, int>(
                StringComparer.Ordinal)
            {
                ["Observe"] = 1,
                ["Warning"] = 2,
                ["Critical"] = 3
            };

            var rows = report.Findings.Select(finding =>
            {
                if (finding is null
                    || string.IsNullOrWhiteSpace(finding.Account)
                    || !finding.Account.Contains('@')
                    || finding.Account.Length > 254
                    || finding.Passed is < 0
                    || finding.Rejected < 0
                    || finding.RejectedPeriods < 0
                    || !levels.ContainsKey(finding.Level)
                    || !reasons.TryGetValue(finding.Reason, out var reason)
                    || finding.RejectionSharePercent is < 0 or > 100
                    || (finding.RejectionSharePercent.HasValue
                        && !double.IsFinite(
                            finding.RejectionSharePercent.Value)))
                {
                    throw new InvalidDataException(
                        "Der Risiko-Endpunkt liefert ungültige Befunde.");
                }

                return new MailSecurityRiskRow(
                    finding.Account,
                    finding.Level,
                    reason,
                    finding.Passed,
                    finding.Rejected,
                    finding.RejectedPeriods,
                    finding.SendingLimitZero,
                    finding.RejectionSharePercent);
            })
            .OrderByDescending(row => levels[row.Level])
            .ThenByDescending(row => row.Rejected)
            .ThenBy(row => row.Account, StringComparer.OrdinalIgnoreCase)
            .ToArray();

            MailSecurityRiskFindings = Array.AsReadOnly(rows);

            var critical = rows.Count(row => row.Level == "Critical");
            var warning = rows.Count(row => row.Level == "Warning");
            var observe = rows.Count(row => row.Level == "Observe");

            MailSecurityRiskStatusText = rows.Length == 0
                ? "Keine Auffälligkeiten nach vorläufigen Testregeln "
                  + $"({report.EvaluatedAccounts:N0} Accounts ausgewertet). "
                  + "Kein Spam-Freigabenachweis."
                : $"Kritisch: {critical} · Warnung: {warning} · "
                  + $"Beobachten: {observe} · "
                  + $"Datenstand: {report.GeneratedAtUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}";
        }
        catch (UnauthorizedAccessException)
        {
            MailSecurityRiskStatusText =
                "Risikoanalyse: Admin-Zugang wurde abgelehnt.";
        }
        catch (NotSupportedException)
        {
            MailSecurityRiskStatusText =
                "Risikoanalyse erst mit dem nächsten Serverupdate verfügbar.";
        }
        catch (HttpRequestException)
        {
            MailSecurityRiskStatusText =
                "Risikoanalyse konnte den Server nicht erreichen.";
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            MailSecurityRiskStatusText =
                "Zeitüberschreitung bei der Risikoanalyse.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            MailSecurityRiskStatusText =
                "Risikobewertung konnte nicht zuverlässig geladen werden.";
        }
    }
}
