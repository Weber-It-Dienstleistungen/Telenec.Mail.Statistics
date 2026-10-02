using System.Net.Http;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public async Task LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await RefreshAsync(
            cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RefreshAsync(
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