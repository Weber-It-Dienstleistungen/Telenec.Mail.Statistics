using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using Telenec.Mail.Statistics.Admin.Models;

namespace Telenec.Mail.Statistics.Admin.ViewModels;

/// <summary>
/// Eigenständiger Mailverkehrsbereich des Hauptfensters.
/// Die Werte stammen ausschließlich aus dem geschützten Server-Endpunkt.
/// Fehlende/veraltete Daten werden nie als "0 E-Mails" interpretiert.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private string mailTrafficStatusText =
        "Mailverkehrsdaten werden geladen …";

    [ObservableProperty]
    private string mailTrafficPeriodText = "Letzte 24 Stunden";

    [ObservableProperty]
    private string mailTrafficDataAgeText = "Datenstand: nicht verfügbar";

    [ObservableProperty]
    private string mailTrafficOutgoingTotalText = "—";

    [ObservableProperty]
    private string mailTrafficIncomingTotalText = "—";

    [ObservableProperty]
    private string mailTrafficOutgoingPeakText = "—";

    [ObservableProperty]
    private string mailTrafficIncomingPeakText = "—";

    [ObservableProperty]
    private IReadOnlyList<MailTrafficChartBar> mailTrafficOutgoingBars =
        EmptyBars();

    [ObservableProperty]
    private IReadOnlyList<MailTrafficChartBar> mailTrafficIncomingBars =
        EmptyBars();

    [ObservableProperty]
    private IReadOnlyList<MailTrafficAccountRow> mailTrafficTopSenders =
        EmptyAccounts();

    [ObservableProperty]
    private IReadOnlyList<MailTrafficAccountRow> mailTrafficTopRecipients =
        EmptyAccounts();

    private async Task LoadMailTrafficAsync(
        CancellationToken cancellationToken)
    {
        // Alte Werte nicht als aktuell weiter anzeigen,
        // falls der neue Abruf scheitert oder die Quelle abgeschaltet wurde.
        MailTrafficStatusText = "Mailverkehrsdaten werden geladen …";
        MailTrafficPeriodText = "Letzte 24 Stunden";
        MailTrafficDataAgeText = "Datenstand: nicht verfügbar";
        MailTrafficOutgoingTotalText = "—";
        MailTrafficIncomingTotalText = "—";
        MailTrafficOutgoingPeakText = "—";
        MailTrafficIncomingPeakText = "—";
        MailTrafficOutgoingBars = EmptyBars();
        MailTrafficIncomingBars = EmptyBars();
        MailTrafficTopSenders = EmptyAccounts();
        MailTrafficTopRecipients = EmptyAccounts();

        try
        {
            var response =
                await _statisticsClient.GetMailTrafficAsync(cancellationToken);

            if (!response.Available)
            {
                MailTrafficStatusText =
                    string.IsNullOrWhiteSpace(response.StatusMessage)
                        ? "Die Datenquelle liefert noch keine Mailverkehrsdaten."
                        : response.StatusMessage;
                return;
            }

            if (response.Hours is not { Count: 24 }
                || response.TopSenders is null
                || response.TopRecipients is null
                || response.WindowEndUtc - response.WindowStartUtc
                    != TimeSpan.FromHours(24))
            {
                throw new InvalidDataException(
                    "Unvollständiger 24-Stunden-Mailverkehrsreport.");
            }

            var hours =
                response.Hours.OrderBy(h => h.HourStartUtc).ToArray();

            for (var i = 0; i < hours.Length; i++)
            {
                if (hours[i] is null
                    || hours[i].HourStartUtc != response.WindowStartUtc.AddHours(i)
                    || hours[i].Outgoing < 0
                    || hours[i].Incoming < 0)
                {
                    throw new InvalidDataException(
                        "Der Mailverkehrsreport enthält inkonsistente Stundenwerte.");
                }
            }

            var outgoingMaximum = hours.Max(h => h.Outgoing);
            var incomingMaximum = hours.Max(h => h.Incoming);

            MailTrafficOutgoingBars =
                CreateBars(hours, outgoing: true, outgoingMaximum);

            MailTrafficIncomingBars =
                CreateBars(hours, outgoing: false, incomingMaximum);

            MailTrafficOutgoingTotalText =
                hours.Sum(h => (long)h.Outgoing).ToString("N0");

            MailTrafficIncomingTotalText =
                hours.Sum(h => (long)h.Incoming).ToString("N0");

            MailTrafficOutgoingPeakText =
                $"Spitze: {outgoingMaximum:N0} / Stunde";

            MailTrafficIncomingPeakText =
                $"Spitze: {incomingMaximum:N0} / Stunde";

            MailTrafficTopSenders =
                CreateRanking(response.TopSenders);

            MailTrafficTopRecipients =
                CreateRanking(response.TopRecipients);

            MailTrafficPeriodText =
                $"{response.WindowStartUtc.ToLocalTime():dd.MM. HH:mm} – " +
                $"{response.WindowEndUtc.ToLocalTime():dd.MM. HH:mm} Uhr";

            MailTrafficDataAgeText =
                response.GeneratedAtUtc.HasValue
                    ? $"Datenstand: {response.GeneratedAtUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm:ss}"
                    : "Datenstand: nicht angegeben";

            MailTrafficStatusText =
                "Aggregierter Mailverkehr verfügbar. " +
                "Die Top-3-Werte sind Hinweise zur technischen Prüfung, kein Spamnachweis.";
        }
        catch (UnauthorizedAccessException)
        {
            MailTrafficStatusText =
                "Mail-Statistik nicht verfügbar: Der Admin-Schlüssel wurde abgelehnt.";
        }
        catch (NotSupportedException)
        {
            MailTrafficStatusText =
                "Der Server bietet den Mail-Statistik-Endpunkt noch nicht an.";
        }
        catch (HttpRequestException)
        {
            MailTrafficStatusText =
                "Mail-Statistik nicht erreichbar. Die bisherigen Bereiche bleiben nutzbar.";
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            MailTrafficStatusText =
                "Zeitüberschreitung beim Abruf der Mailverkehrsdaten.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            MailTrafficStatusText =
                "Der Mailverkehrsreport konnte nicht zuverlässig ausgewertet werden.";
        }
    }

    private static IReadOnlyList<MailTrafficChartBar> CreateBars(
        IReadOnlyList<MailTrafficHourResponse> hours,
        bool outgoing,
        int maximum)
    {
        return Array.AsReadOnly(
            hours.Select((hour, index) =>
            {
                var count = outgoing ? hour.Outgoing : hour.Incoming;
                var height = maximum <= 0 || count == 0
                    ? 0d
                    : Math.Max(3d, 150d * count / maximum);

                var localStart = hour.HourStartUtc.ToLocalTime();
                var localEnd = hour.HourStartUtc.AddHours(1).ToLocalTime();

                return new MailTrafficChartBar(
                    BarHeight: height,
                    AxisLabel: index % 4 == 0
                        ? localStart.ToString("HH:mm")
                        : string.Empty,
                    Tooltip:
                        $"{localStart:dd.MM. HH:mm}–{localEnd:HH:mm}: {count:N0} E-Mails");
            }).ToArray());
    }

    private static IReadOnlyList<MailTrafficAccountRow> CreateRanking(
        IReadOnlyList<MailTrafficAccountResponse> accounts)
    {
        var entries = accounts
            .Where(a =>
                a is not null
                && !string.IsNullOrWhiteSpace(a.Account)
                && a.Count >= 0)
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.Account, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select((a, i) =>
                new MailTrafficAccountRow(
                    Rank: $"{i + 1}.",
                    Account: a.Account,
                    CountText: $"{a.Count:N0}"))
            .ToArray();

        return entries.Length > 0
            ? Array.AsReadOnly(entries)
            : EmptyAccounts();
    }

    private static IReadOnlyList<MailTrafficChartBar> EmptyBars()
    {
        return Array.AsReadOnly(
            Enumerable.Range(0, 24)
                .Select(_ => new MailTrafficChartBar(
                    BarHeight: 0d,
                    AxisLabel: string.Empty,
                    Tooltip: "Noch keine Mailverkehrsdaten vorhanden."))
                .ToArray());
    }

    private static IReadOnlyList<MailTrafficAccountRow> EmptyAccounts() =>
        Array.AsReadOnly(new[]
        {
            new MailTrafficAccountRow(
                Rank: "—",
                Account: "Keine Accountdaten verfügbar",
                CountText: "—")
        });
}

public sealed record MailTrafficChartBar(
    double BarHeight,
    string AxisLabel,
    string Tooltip);

public sealed record MailTrafficAccountRow(
    string Rank,
    string Account,
    string CountText);
