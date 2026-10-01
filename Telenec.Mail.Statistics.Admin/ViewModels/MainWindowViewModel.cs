using CommunityToolkit.Mvvm.ComponentModel;
using System.Net.Http;
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

    public string WindowTitle => "Telenec Mail Statistik";

    public string DashboardTitle => "Nutzungsstatistik";

    public string DashboardSubtitle =>
        "Übersicht der aktiven Telenec-Mail-Installationen";

    [ObservableProperty]
    private string statusText = "Daten werden geladen …";

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
        StatusText = "Daten werden geladen …";

        try
        {
            var statistics =
                await _statisticsClient.GetDashboardStatisticsAsync(
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

            StatusText =
                $"Daten erfolgreich geladen – {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        }
        catch (UnauthorizedAccessException)
        {
            StatusText =
                "Zugriff verweigert – der Admin-API-Schlüssel wurde abgelehnt.";
        }
        catch (HttpRequestException)
        {
            StatusText =
                "Statistikserver ist derzeit nicht erreichbar.";
        }
        catch (TaskCanceledException)
        {
            StatusText =
                "Die Anfrage an den Statistikserver hat zu lange gedauert.";
        }
        catch (Exception)
        {
            StatusText =
                "Die Statistikdaten konnten nicht geladen werden.";
        }
    }
}