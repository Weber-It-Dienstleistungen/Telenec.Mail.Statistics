
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

    // Bestehende Statistikberechnungen.

    public double ActiveTodayPercentage =>
        GetPercentage(ActiveToday, TotalInstallations);

    public double ActiveLast7DaysPercentage =>
        GetPercentage(ActiveLast7Days, TotalInstallations);

    public double ActiveLast30DaysPercentage =>
        GetPercentage(ActiveLast30Days, TotalInstallations);

    public double InactiveOver30DaysPercentage =>
        GetPercentage(InactiveOver30Days, TotalInstallations);

    public int ActiveDays1To7 =>
        Math.Max(0, ActiveLast7Days - ActiveToday);

    public int ActiveDays8To30 =>
        Math.Max(0, ActiveLast30Days - ActiveLast7Days);

    public double ActiveDays1To7Percentage =>
        GetPercentage(ActiveDays1To7, TotalInstallations);

    public double ActiveDays8To30Percentage =>
        GetPercentage(ActiveDays8To30, TotalInstallations);

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

    // Bestehende Nutzungsstatistik.

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

    // Bestehende Reputationsanzeige.

    public string ReputationTitle =>
        "Mailserver-Reputation";

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

    // Dynamische Blacklist-Übersicht.
    //
    // Keine fest hinterlegten Listennamen.
    // Der Server bestimmt Umfang und Inhalt.

    [ObservableProperty]
    private string reputationScopeText =
        "Prüfumfang wird geladen …";

    [ObservableProperty]
    private string reputationSummaryText =
        "Noch keine Übersicht geladen.";

    [ObservableProperty]
    private bool isReputationOverviewAvailable;

    [ObservableProperty]
    private IReadOnlyList<ReputationBlacklistRow>
        reputationEntries =
            Array.Empty<ReputationBlacklistRow>();

    // Manueller Blacklist-Check.

    [ObservableProperty]
    private bool isReputationChecking;

    [ObservableProperty]
    private string reputationCheckMessage =
        "Die Blacklist-Prüfung kann manuell gestartet werden.";

    // Die bestehenden Einstiegs- und
    // Aktualisierungsfunktionen bleiben erhalten.

    public async Task LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RefreshAsync(
        CancellationToken cancellationToken)
    {
        await LoadStatisticsAsync(cancellationToken);

        await LoadReputationAsync(cancellationToken);
    }

    // Der Server-Check und das Laden seiner Ergebnisse
    // sind zwei getrennte Vorgänge.
    //
    // Eine Erfolgsmeldung erscheint nur, wenn beide
    // Vorgänge erfolgreich abgeschlossen wurden.

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task CheckReputationAsync(
        CancellationToken cancellationToken)
    {
        if (IsReputationChecking)
        {
            return;
        }

        IsReputationChecking = true;

        ReputationCheckMessage =
            "Blacklist-Prüfung läuft auf dem Server …";

        try
        {
            await _statisticsClient
                .TriggerReputationCheckAsync(
                    cancellationToken);

            ReputationCheckMessage =
                "Prüfung abgeschlossen. Ergebnisse werden geladen …";

            var resultsLoaded =
                await LoadReputationAsync(
                    cancellationToken);

            ReputationCheckMessage = resultsLoaded
                ? "Blacklist-Prüfung abgeschlossen. " +
                  "Die Ergebnisübersicht wurde aktualisiert."
                : "Die serverseitige Blacklist-Prüfung wurde abgeschlossen, " +
                  "aber die Ergebnisübersicht konnte nicht geladen werden. " +
                  "Bitte die Ansicht über „Aktualisieren“ erneut laden.";
        }
        catch (UnauthorizedAccessException)
        {
            ReputationCheckMessage =
                "Prüfung nicht gestartet: " +
                "Der Admin-API-Schlüssel wurde abgelehnt.";
        }
        catch (NotSupportedException exception)
        {
            ReputationCheckMessage =
                exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            ReputationCheckMessage =
                exception.Message;
        }
        catch (HttpRequestException)
        {
            ReputationCheckMessage =
                "Blacklist-Prüfung fehlgeschlagen: " +
                "Der Statistikserver ist nicht erreichbar " +
                "oder hat einen HTTP-Fehler gemeldet.";
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            ReputationCheckMessage =
                "Die Anfrage wurde abgebrochen. " +
                "Ob die serverseitige Prüfung abgeschlossen wurde, " +
                "kann nicht bestätigt werden.";
        }
        catch (TaskCanceledException)
        {
            ReputationCheckMessage =
                "Zeitüberschreitung bei der Blacklist-Prüfung. " +
                "Der serverseitige Zustand ist unklar. " +
                "Bitte die Ergebnisse erneut laden.";
        }
        catch (Exception)
        {
            ReputationCheckMessage =
                "Die manuelle Blacklist-Prüfung konnte " +
                "nicht erfolgreich abgeschlossen werden.";
        }
        finally
        {
            IsReputationChecking = false;
        }
    }

    // Bestehende Nutzungsstatistik.

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

    // Reputationsabfrage.
    //
    // Dieser Bereich wird unabhängig von
    // der Nutzungsstatistik behandelt.
    //
    // Rückgabewert:
    //
    // true  = API-Abruf und Verarbeitung erfolgreich.
    // false = API-Abruf oder Verarbeitung fehlgeschlagen.
    //
    // Ein fachlicher Blacklist-Status "Listed"
    // ist dabei kein Fehler beim Datenabruf.

    private async Task<bool> LoadReputationAsync(
        CancellationToken cancellationToken)
    {
        IsReputationLoading = true;

        ResetReputationDisplay();

        try
        {
            var reputation =
                await _statisticsClient
                    .GetReputationAsync(
                        cancellationToken);

            ApplyReputation(reputation);

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            SetReputationError(
                "Zugriff verweigert",
                "Der Admin-API-Schlüssel wurde vom Server abgelehnt.");

            return false;
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            SetReputationError(
                "Reputations-API nicht verfügbar",
                "Der angesprochene Statistikserver unterstützt den Reputations-Endpunkt noch nicht.");

            return false;
        }
        catch (HttpRequestException)
        {
            SetReputationError(
                "Reputationsserver nicht erreichbar",
                "Die Reputationsdaten konnten nicht vom Statistikserver abgerufen werden.");

            return false;
        }
        catch (TaskCanceledException)
        {
            SetReputationError(
                "Zeitüberschreitung",
                "Die Reputationsabfrage hat zu lange gedauert.");

            return false;
        }
        catch (JsonException)
        {
            SetReputationError(
                "Ungültige API-Antwort",
                "Der Statistikserver hat Reputationsdaten in einem unbekannten Format geliefert.");

            return false;
        }
        catch (InvalidDataException)
        {
            SetReputationError(
                "Unvollständige API-Antwort",
                "Die Reputationsdaten des Statistikservers sind unvollständig.");

            return false;
        }
        catch (Exception)
        {
            SetReputationError(
                "Reputationsabfrage fehlgeschlagen",
                "Beim Laden der Mailserver-Reputation ist ein unerwarteter Fehler aufgetreten.");

            return false;
        }
        finally
        {
            IsReputationLoading = false;
        }
    }

    private void ResetReputationDisplay()
    {
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

        // Keine alten grünen Statussymbole
        // während einer erneuten Abfrage anzeigen.

        ReputationScopeText =
            "Prüfumfang wird geladen …";

        ReputationSummaryText =
            "Noch keine Übersicht geladen.";

        IsReputationOverviewAvailable = false;

        ReputationEntries =
            Array.Empty<ReputationBlacklistRow>();
    }

    // Bestehendes Einzelprüfergebnis übernehmen.
    //
    // Die PSBL-Anzeige bleibt kompatibel
    // mit unserem bisherigen Serververtrag.

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
            FormatDate(reputation.LastAttemptUtc);

        ReputationLastSuccessfulCheckText =
            FormatDate(reputation.LastSuccessfulCheckUtc);

        ReputationProviderResponse =
            string.IsNullOrWhiteSpace(
                reputation.Result.ProviderResponse)
                ? "Keine"
                : reputation.Result.ProviderResponse;

        var result = reputation.Result;

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
                    "Dies ist keine Aussage über andere Blacklists.";

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

        // Zusätzlich die dynamische Übersicht
        // aufbereiten, sofern sie vorhanden ist.

        ApplyReputationOverview(reputation);
    }

    private void ApplyReputationOverview(
        ReputationMonitoringResponse reputation)
    {
        var overview = reputation.Overview;

        /*
         * Rückwärtskompatibilität:
         *
         * Ein älterer Server kennt "overview"
         * möglicherweise noch nicht.
         *
         * Wir zeigen dann das vorhandene
         * Einzelprüfergebnis, behaupten aber
         * keine vollständige Katalogabdeckung.
         */

        if (overview is null)
        {
            var result = reputation.Result;

            var legacyEntry =
                new ReputationBlacklistEntryResponse
                {
                    Id = result.BlacklistId,

                    DisplayName =
                        result.BlacklistId.ToUpperInvariant(),

                    Status = result.Status,

                    CheckedTarget = result.CheckedTarget,

                    StartedAtUtc = result.StartedAtUtc,

                    CompletedAtUtc = result.CompletedAtUtc,

                    ProviderResponse = result.ProviderResponse,

                    ErrorMessage = result.ErrorMessage,

                    IsConclusive = result.IsConclusive,

                    IsListed = result.IsListed
                };

            ReputationEntries =
                new[]
                {
                    CreateBlacklistRow(legacyEntry)
                };

            ReputationScopeText =
                "Einzelprüfung – vollständige Übersicht auf diesem Server noch nicht verfügbar";

            ReputationSummaryText =
                "Die Serverversion liefert noch keinen dynamischen Blacklist-Katalog.";

            IsReputationOverviewAvailable = false;

            return;
        }

        /*
         * Grundlegende Validierung.
         *
         * Eine leere, beschädigte oder
         * widersprüchliche Übersicht wird
         * nicht als gültiger Katalog verwendet.
         */

        if (overview.Entries is null
            || overview.TotalCount != overview.Entries.Count
            || overview.TotalCount < 0
            || overview.IntegratedCount < 0
            || overview.NotIntegratedCount < 0
            || overview.IntegratedCount
               + overview.NotIntegratedCount
               != overview.TotalCount)
        {
            throw new InvalidDataException(
                "Die Blacklist-Übersicht enthält widersprüchliche Kennzahlen.");
        }

        if (overview.Entries.Any(entry =>
                entry is null
                || string.IsNullOrWhiteSpace(entry.Id)
                || string.IsNullOrWhiteSpace(entry.DisplayName)
                || string.IsNullOrWhiteSpace(entry.Status)))
        {
            throw new InvalidDataException(
                "Die Blacklist-Übersicht enthält ungültige Einträge.");
        }

        if (overview.Entries
            .Select(entry => entry.Id)
            .Distinct(StringComparer.Ordinal)
            .Count() != overview.TotalCount)
        {
            throw new InvalidDataException(
                "Die Blacklist-Übersicht enthält doppelte Kennungen.");
        }

        /*
         * Jede Statuszeile wird aus den
         * aktuellen Serverdaten erzeugt.
         *
         * Sortierung:
         *
         * 0 = gelistet
         * 1 = Fehler / unbekannt
         * 2 = integriert, noch nicht geprüft
         * 3 = nicht gelistet
         * 4 = nicht integriert
         *
         * PSBL bleibt damit sichtbar,
         * auch wenn 67 graue Einträge folgen.
         */

        var rows =
            overview.Entries
                .Select(CreateBlacklistRow)
                .OrderBy(row => row.SortOrder)
                .ThenBy(
                    row => row.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        ReputationEntries =
            Array.AsReadOnly(rows);

        ReputationScopeText =
            $"{overview.IntegratedCount} von {overview.TotalCount} Referenzlisten angebunden";

        ReputationSummaryText =
            $"{overview.NotListedCount} nicht gelistet · " +
            $"{overview.ListedCount} gelistet · " +
            $"{overview.WarningCount} Warnungen · " +
            $"{overview.NotCheckedCount} ungeprüft · " +
            $"{overview.NotIntegratedCount} nicht integriert";

        IsReputationOverviewAvailable = true;
    }

    // Statuszuordnung für die kompakte Liste.
    //
    // Nur eindeutig erfolgreiche Ergebnisse
    // erhalten grüne oder rote Symbole.
    //
    // Widersprüche und unbekannte Werte
    // werden als Warnung gekennzeichnet.

    private static ReputationBlacklistRow CreateBlacklistRow(
        ReputationBlacklistEntryResponse entry)
    {
        var (
            statusKind,
            statusSymbol,
            statusText,
            sortOrder) = entry.Status switch
            {
                "Listed"
                    when entry.IsConclusive
                         && entry.IsListed == true
                    => ("Error", "✖", "Gelistet", 0),

                "NotListed"
                    when entry.IsConclusive
                         && entry.IsListed == false
                    => ("Success", "✔", "Nicht gelistet", 3),

                "NotIntegrated"
                    => ("Neutral", "—", "Nicht integriert", 4),

                "NotChecked"
                    => ("Neutral", "—", "Noch nicht geprüft", 2),

                "Failed"
                    => ("Warning", "⚠", "Prüfung fehlgeschlagen", 1),

                "AccessDenied"
                    => ("Warning", "⚠", "Zugriff verweigert", 1),

                "Unknown"
                    => ("Warning", "⚠", "Ergebnis unbekannt", 1),

                _
                    => ("Warning", "⚠", "Widersprüchlicher oder unbekannter Status", 1)
            };

        var details =
            $"{entry.DisplayName}\n" +
            $"Status: {statusText}\n" +
            $"Prüfziel: {entry.CheckedTarget ?? "Nicht vorhanden"}\n" +
            $"Letzter Abschluss: {FormatDate(entry.CompletedAtUtc)}\n" +
            $"Anbieterantwort: {entry.ProviderResponse ?? "Keine"}";

        if (!string.IsNullOrWhiteSpace(entry.ErrorMessage))
        {
            details +=
                $"\nFehler: {entry.ErrorMessage}";
        }

        return new ReputationBlacklistRow(
            Id: entry.Id,
            DisplayName: entry.DisplayName,
            Status: entry.Status,
            StatusKind: statusKind,
            StatusSymbol: statusSymbol,
            StatusText: statusText,
            SortOrder: sortOrder,
            CheckedTarget: entry.CheckedTarget,
            CompletedAtText: FormatDate(entry.CompletedAtUtc),
            ProviderResponse: entry.ProviderResponse,
            ErrorMessage: entry.ErrorMessage,
            TargetKind: entry.TargetKind,
            AccessStatus: entry.AccessStatus,
            DetailsText: details);
    }

    private void SetReputationError(
        string status,
        string description)
    {
        ReputationStatusKind = "Error";
        ReputationStatusText = status;
        ReputationDetailText = description;

        // Bei einem fehlgeschlagenen API-Abruf
        // keine alten Prüfergebnisse anzeigen.

        ReputationEntries =
            Array.Empty<ReputationBlacklistRow>();

        ReputationScopeText =
            "Prüfumfang nicht verfügbar";

        ReputationSummaryText =
            "Blacklist-Übersicht konnte nicht geladen werden.";

        IsReputationOverviewAvailable = false;
    }

    private static string GetErrorDescription(
        string? providerError,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(providerError)
            ? fallback
            : providerError;
    }

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

    // Bestehende Diagramm-Benachrichtigungen.

    private void RaiseChartPropertiesChanged()
    {
        OnPropertyChanged(nameof(ActiveTodayPercentage));
        OnPropertyChanged(nameof(ActiveLast7DaysPercentage));
        OnPropertyChanged(nameof(ActiveLast30DaysPercentage));
        OnPropertyChanged(nameof(InactiveOver30DaysPercentage));

        OnPropertyChanged(nameof(ActiveDays1To7));
        OnPropertyChanged(nameof(ActiveDays8To30));

        OnPropertyChanged(nameof(ActiveDays1To7Percentage));
        OnPropertyChanged(nameof(ActiveDays8To30Percentage));

        OnPropertyChanged(nameof(ActiveTodayChartText));
        OnPropertyChanged(nameof(ActiveLast7DaysChartText));
        OnPropertyChanged(nameof(ActiveLast30DaysChartText));
        OnPropertyChanged(nameof(InactiveOver30DaysChartText));

        OnPropertyChanged(nameof(ActiveDays1To7ChartText));
        OnPropertyChanged(nameof(ActiveDays8To30ChartText));
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
                informationalVersion.IndexOf('+');

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

/// <summary>
/// Fertig aufbereitete Anzeigezeile für
/// die dynamische WPF-Blacklist-Liste.
///
/// Die Oberfläche muss keine eigenen
/// Statusentscheidungen treffen.
///
/// StatusKind und StatusSymbol können
/// direkt per WPF-Binding verwendet werden.
///
/// DetailsText ist für eine spätere
/// Detailansicht oder einen Tooltip vorgesehen.
/// </summary>
public sealed record ReputationBlacklistRow(
    string Id,
    string DisplayName,
    string Status,
    string StatusKind,
    string StatusSymbol,
    string StatusText,
    int SortOrder,
    string? CheckedTarget,
    string CompletedAtText,
    string? ProviderResponse,
    string? ErrorMessage,
    string TargetKind,
    string AccessStatus,
    string DetailsText);
