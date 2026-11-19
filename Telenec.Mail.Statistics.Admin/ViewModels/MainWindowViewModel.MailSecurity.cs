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
}
