using System.Net.Http;
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

    public string WindowTitle => "Telenec Mail Statistik";

    public string DashboardTitle => "Nutzungsstatistik";

    public string DashboardSubtitle =>
        "Übersicht der aktiven Telenec-Mail-Installationen";

    [ObservableProperty]
    private string statusText = "Daten werden geladen …";

    [ObservableProperty]
    private string statusKind = "Loading";

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
        await RefreshAsync(cancellationToken);
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
}