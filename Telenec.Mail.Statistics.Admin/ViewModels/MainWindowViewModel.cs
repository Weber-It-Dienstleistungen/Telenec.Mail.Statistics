
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Telenec.Mail.Statistics.Admin.Models;
using Telenec.Mail.Statistics.Admin.Services;

namespace Telenec.Mail.Statistics.Admin.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IAdminStatisticsClient _statisticsClient;

    public MainWindowViewModel(
        IAdminStatisticsClient statisticsClient)
    {
        _statisticsClient = statisticsClient;
    }

    public string WindowTitle =>
        "Telenec Mail Statistik";

    public string DashboardTitle =>
        "Nutzungsstatistik";

    public string DashboardSubtitle =>
        "Übersicht der aktiven Telenec-Mail-Installationen";

    public string ApplicationVersionText =>
        $"Telenec Mail · {GetApplicationVersion()}";

    /*
     * Bestehende Statistikberechnungen.
     *
     * Diese bleiben unverändert.
     */

    public double ActiveTodayPercentage =>
        GetPercentage(
            ActiveToday,
            TotalInstallations);

    public double ActiveLast7DaysPercentage =>
        GetPercentage(
            ActiveLast7Days,
            TotalInstallations);

    public double ActiveLast30DaysPercentage =>
        GetPercentage(
            ActiveLast30Days,
            TotalInstallations);

    public double InactiveOver30DaysPercentage =>
        GetPercentage(
            InactiveOver30Days,
            TotalInstallations);

    public int ActiveDays1To7 =>
        Math.Max(
            0,
            ActiveLast7Days - ActiveToday);

    public int ActiveDays8To30 =>
        Math.Max(
            0,
            ActiveLast30Days - ActiveLast7Days);

    public double ActiveDays1To7Percentage =>
        GetPercentage(
            ActiveDays1To7,
            TotalInstallations);

    public double ActiveDays8To30Percentage =>
        GetPercentage(
            ActiveDays8To30,
            TotalInstallations);

    public string ActiveTodayChartText =>
        FormatChartValue(
            ActiveToday,
            ActiveTodayPercentage);

    public string ActiveLast7DaysChartText =>
        FormatChartValue(
            ActiveLast7Days,
            ActiveLast7DaysPercentage);

    public string ActiveLast30DaysChartText =>
        FormatChartValue(
            ActiveLast30Days,
            ActiveLast30DaysPercentage);

    public string InactiveOver30DaysChartText =>
        FormatChartValue(
            InactiveOver30Days,
            InactiveOver30DaysPercentage);

    public string ActiveDays1To7ChartText =>
        FormatChartValue(
            ActiveDays1To7,
            ActiveDays1To7Percentage);

    public string ActiveDays8To30ChartText =>
        FormatChartValue(
            ActiveDays8To30,
            ActiveDays8To30Percentage);

    /*
     * Bestehende Eigenschaften der Nutzungsstatistik.
     */

    [ObservableProperty]
    private string statusText =
        "Daten werden geladen …";

    [ObservableProperty]
    private string statusKind =
        "Loading";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private int totalInstallations;

    [ObservableProperty]
    private int activeToday;

    [ObservableProperty]
    private int activeLast7Days;

    [ObservableProperty]
    private int activeLast30Days;

    [ObservableProperty]
    private int inactiveOver30Days;

    /*
     * Neue Eigenschaften für die Mailserver-Reputation.
     *
     * Die Darstellung wird im folgenden Sprint
     * mit diesen Eigenschaften verbunden.
     *
     * Aktuell ist ausschließlich PSBL angebunden.
     */

    public string ReputationTitle =>
        "Mailserver-Reputation";

    public string ReputationScopeText =>
        "1 von 68 Referenzlisten angebunden – PSBL";

    [ObservableProperty]
    private bool isReputationLoading;

    [ObservableProperty]
    private string reputationStatusKind =
        "NotChecked";

    [ObservableProperty]
    private string reputationStatusText =
        "Noch keine Reputationsdaten geladen.";

    [ObservableProperty]
    private string reputationHostname =
        "Nicht verfügbar";

    [ObservableProperty]
    private string reputationIpAddress =
        "Nicht verfügbar";

    [ObservableProperty]
    private string reputationResolvedAddresses =
        "Nicht verfügbar";

    [ObservableProperty]
    private string reputationBlacklistName =
        "PSBL";

    [ObservableProperty]
    private string reputationLastAttemptText =
        "Noch keine Prüfung";

    [ObservableProperty]
    private string reputationLastSuccessfulCheckText =
        "Noch keine erfolgreiche Prüfung";

    [ObservableProperty]
    private string reputationProviderResponse =
        "Keine";

    [ObservableProperty]
    private string reputationDetailText =
        "Die Überwachung wird geladen.";

    /*
     * Gemeinsamer Ladeeinstieg.
     *
     * MainWindow.xaml.cs verwendet bereits LoadAsync.
     * Eine Anpassung des Code-behind ist nicht nötig.
     */

    public async Task LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await RefreshAsync(
            cancellationToken);
    }

    /*
     * Gemeinsame Aktualisierung.
     *
     * Die beiden Datenbereiche werden unabhängig
     * voneinander geladen.
     *
     * Ein Fehler der Reputationsabfrage
     * verändert niemals die Statistikkennzahlen.
     *
     * Der vorhandene Aktualisieren-Button
     * verwendet weiterhin RefreshCommand.
     */

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RefreshAsync(
        CancellationToken cancellationToken)
    {
        await LoadStatisticsAsync(
            cancellationToken);

        await LoadReputationAsync(
            cancellationToken);
    }

    /*
     * Bestehende Nutzungsstatistik.
     *
     * Die bisherige Lade- und Fehlerbehandlung
     * bleibt fachlich unverändert.
     */

    private async Task LoadStatisticsAsync(
        CancellationToken cancellationToken)
    {
        IsLoading = true;
        StatusKind = "Loading";
        StatusText = "Daten werden geladen …";

        try
        {
            var statistics =
                await _statisticsClient
                    .GetDashboardStatisticsAsync(
                        cancellationToken);

            TotalInstallations =
                statistics.TotalInstallations;

            ActiveToday =
                statistics.ActiveToday;

            ActiveLast7Days =
                statistics.ActiveLast7Days;

            ActiveLast30Days =
                statistics.ActiveLast30Days;

            InactiveOver30Days =
                statistics.InactiveOver30Days;

            RaiseChartPropertiesChanged();

            StatusKind = "Success";

            StatusText =
                $"Daten erfolgreich geladen – {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        }
        catch (UnauthorizedAccessException)
        {
            StatusKind = "Error";
            StatusText =
                "Zugriff verweigert – der Admin-API-Schlüssel wurde abgelehnt.";
        }
        catch (HttpRequestException)
        {
            StatusKind = "Error";
            StatusText =
                "Statistikserver ist derzeit nicht erreichbar.";
        }
        catch (TaskCanceledException)
        {
            StatusKind = "Warning";
            StatusText =
                "Die Anfrage an den Statistikserver hat zu lange gedauert.";
        }
        catch (Exception)
        {
            StatusKind = "Error";
            StatusText =
                "Die Statistikdaten konnten nicht geladen werden.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /*
     * Neue Reputationsabfrage.
     *
     * Dieser Bereich besitzt eine eigene
     * Status- und Fehlerverwaltung.
     *
     * Dadurch bleiben die bestehenden
     * Nutzungsstatistikdaten unabhängig.
     */

    private async Task LoadReputationAsync(
        CancellationToken cancellationToken)
    {
        IsReputationLoading = true;

        /*
         * Frühere Ergebnisse werden zunächst
         * zurückgesetzt.
         *
         * Dadurch darf beispielsweise ein
         * früherer grüner Status nicht stehen
         * bleiben, wenn der aktuelle Abruf
         * fehlschlägt.
         */

        ReputationStatusKind = "Loading";

        ReputationStatusText =
            "Reputationsdaten werden geladen …";

        ReputationHostname = "Nicht verfügbar";
        ReputationIpAddress = "Nicht verfügbar";

        ReputationResolvedAddresses =
            "Nicht verfügbar";

        ReputationBlacklistName = "PSBL";

        ReputationLastAttemptText =
            "Nicht verfügbar";

        ReputationLastSuccessfulCheckText =
            "Nicht verfügbar";

        ReputationProviderResponse = "Keine";

        ReputationDetailText =
            "Aktueller Monitoring-Zustand wird abgerufen.";

        try
        {
            var reputation =
                await _statisticsClient
                    .GetReputationAsync(
                        cancellationToken);

            ApplyReputation(
                reputation);
        }
        catch (UnauthorizedAccessException)
        {
            SetReputationError(
                "Zugriff verweigert",
                "Der Admin-API-Schlüssel wurde vom Server abgelehnt.");
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            SetReputationError(
                "Reputations-API nicht verfügbar",
                "Der angesprochene Statistikserver unterstützt den Reputations-Endpunkt noch nicht.");
        }
        catch (HttpRequestException)
        {
            SetReputationError(
                "Reputationsserver nicht erreichbar",
                "Die Reputationsdaten konnten nicht vom Statistikserver abgerufen werden.");
        }
        catch (TaskCanceledException)
        {
            SetReputationError(
                "Zeitüberschreitung",
                "Die Reputationsabfrage hat zu lange gedauert.");
        }
        catch (JsonException)
        {
            SetReputationError(
                "Ungültige API-Antwort",
                "Der Statistikserver hat Reputationsdaten in einem unbekannten Format geliefert.");
        }
        catch (InvalidDataException)
        {
            SetReputationError(
                "Unvollständige API-Antwort",
                "Die Reputationsdaten des Statistikservers sind unvollständig.");
        }
        catch (Exception)
        {
            SetReputationError(
                "Reputationsabfrage fehlgeschlagen",
                "Beim Laden der Mailserver-Reputation ist ein unerwarteter Fehler aufgetreten.");
        }
        finally
        {
            IsReputationLoading = false;
        }
    }

    /// <summary>
    /// Übernimmt einen gültig empfangenen
    /// Monitoring-Snapshot in die Eigenschaften
    /// der WPF-Oberfläche.
    ///
    /// Der Listungsstatus wird ausdrücklich
    /// nur für die geprüfte Einzelliste bewertet.
    /// </summary>
    private void ApplyReputation(
        ReputationMonitoringResponse reputation)
    {
        ReputationHostname =
            reputation.Hostname;

        ReputationIpAddress =
            reputation.ExpectedIpv4;

        ReputationResolvedAddresses =
            reputation.ResolvedIpv4Addresses is { Count: > 0 }
                ? string.Join(
                    ", ",
                    reputation.ResolvedIpv4Addresses)
                : "Keine IPv4-Adresse ermittelt";

        ReputationBlacklistName =
            reputation.Result.BlacklistId;

        ReputationLastAttemptText =
            FormatDate(
                reputation.LastAttemptUtc);

        ReputationLastSuccessfulCheckText =
            FormatDate(
                reputation.LastSuccessfulCheckUtc);

        ReputationProviderResponse =
            string.IsNullOrWhiteSpace(
                reputation.Result.ProviderResponse)
                ? "Keine"
                : reputation.Result.ProviderResponse;

        var result = reputation.Result;

        /*
         * Der Status und IsListed müssen
         * fachlich zueinander passen.
         *
         * Widersprüchliche Angaben werden
         * nicht als erfolgreich bewertet.
         */

        switch (result.Status)
        {
            case "NotListed"
                when result.IsConclusive
                     && result.IsListed == false:

                ReputationStatusKind = "Success";

                ReputationStatusText =
                    "PSBL: Nicht gelistet";

                ReputationDetailText =
                    "Die geprüfte Versand-IP ist bei PSBL nicht gelistet. " +
                    "Dies ist keine Aussage über die übrigen 67 Referenzlisten.";

                break;

            case "Listed"
                when result.IsConclusive
                     && result.IsListed == true:

                ReputationStatusKind = "Error";

                ReputationStatusText =
                    "PSBL: Blacklist-Eintrag erkannt";

                ReputationDetailText =
                    "Die geprüfte Versand-IP ist bei PSBL gelistet. " +
                    "Der Eintrag sollte administrativ untersucht werden.";

                break;

            case "NotChecked":

                ReputationStatusKind = "NotChecked";

                ReputationStatusText =
                    "Noch keine Blacklist-Prüfung";

                ReputationDetailText =
                    "Der Monitoring-Dienst hat noch kein Prüfergebnis bereitgestellt.";

                break;

            case "Failed":

                ReputationStatusKind = "Warning";

                ReputationStatusText =
                    "PSBL-Prüfung fehlgeschlagen";

                ReputationDetailText =
                    GetErrorDescription(
                        result.ErrorMessage,
                        "Die Blacklist-Prüfung konnte nicht erfolgreich abgeschlossen werden.");

                break;

            case "AccessDenied":

                ReputationStatusKind = "Warning";

                ReputationStatusText =
                    "PSBL-Zugriff verweigert";

                ReputationDetailText =
                    GetErrorDescription(
                        result.ErrorMessage,
                        "Der Prüfanbieter hat den Zugriff nicht zugelassen.");

                break;

            case "Unknown":

                ReputationStatusKind = "Warning";

                ReputationStatusText =
                    "PSBL-Ergebnis unbekannt";

                ReputationDetailText =
                    GetErrorDescription(
                        result.ErrorMessage,
                        "Die Antwort des Prüfanbieters konnte nicht eindeutig ausgewertet werden.");

                break;

            default:

                ReputationStatusKind = "Warning";

                ReputationStatusText =
                    "Reputationsstatus nicht eindeutig";

                ReputationDetailText =
                    "Der Statistikserver hat einen unbekannten oder widersprüchlichen Prüfstatus geliefert.";

                break;
        }
    }

    /// <summary>
    /// Setzt einen eigenen Fehlerzustand
    /// für die Reputationsanzeige.
    ///
    /// Die Nutzungsstatistik wird dadurch
    /// nicht beeinflusst.
    /// </summary>
    private void SetReputationError(
        string status,
        string description)
    {
        ReputationStatusKind = "Error";
        ReputationStatusText = status;
        ReputationDetailText = description;
    }

    private static string GetErrorDescription(
        string? providerError,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(providerError)
            ? fallback
            : providerError;
    }

    /// <summary>
    /// UTC-Zeitpunkte werden für die Anzeige
    /// in die lokale Zeitzone umgerechnet.
    /// </summary>
    private static string FormatDate(
        DateTimeOffset? timestamp)
    {
        if (!timestamp.HasValue)
        {
            return "Noch keine Prüfung";
        }

        return timestamp.Value
            .ToLocalTime()
            .ToString("dd.MM.yyyy HH:mm:ss");
    }

    /*
     * Bestehende Benachrichtigungen
     * für die Statistikdiagramme.
     */

    private void RaiseChartPropertiesChanged()
    {
        OnPropertyChanged(
            nameof(ActiveTodayPercentage));

        OnPropertyChanged(
            nameof(ActiveLast7DaysPercentage));

        OnPropertyChanged(
            nameof(ActiveLast30DaysPercentage));

        OnPropertyChanged(
            nameof(InactiveOver30DaysPercentage));

        OnPropertyChanged(
            nameof(ActiveDays1To7));

        OnPropertyChanged(
            nameof(ActiveDays8To30));

        OnPropertyChanged(
            nameof(ActiveDays1To7Percentage));

        OnPropertyChanged(
            nameof(ActiveDays8To30Percentage));

        OnPropertyChanged(
            nameof(ActiveTodayChartText));

        OnPropertyChanged(
            nameof(ActiveLast7DaysChartText));

        OnPropertyChanged(
            nameof(ActiveLast30DaysChartText));

        OnPropertyChanged(
            nameof(InactiveOver30DaysChartText));

        OnPropertyChanged(
            nameof(ActiveDays1To7ChartText));

        OnPropertyChanged(
            nameof(ActiveDays8To30ChartText));
    }

    private static double GetPercentage(
        int value,
        int total)
    {
        if (total <= 0)
        {
            return 0;
        }

        return Math.Clamp(
            value * 100d / total,
            0,
            100);
    }

    private static string FormatChartValue(
        int value,
        double percentage)
    {
        return $"{value} · {percentage:0.#} %";
    }

    private static string GetApplicationVersion()
    {
        var assembly =
            typeof(MainWindowViewModel).Assembly;

        var informationalVersion =
            assembly
                .GetCustomAttribute<
                    AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(
                informationalVersion))
        {
            var metadataSeparatorIndex =
                informationalVersion.IndexOf(
                    '+');

            if (metadataSeparatorIndex >= 0)
            {
                informationalVersion =
                    informationalVersion[
                        ..metadataSeparatorIndex];
            }

            return informationalVersion;
        }

        return assembly
                   .GetName()
                   .Version?
                   .ToString()
               ?? "unbekannt";
    }
}
