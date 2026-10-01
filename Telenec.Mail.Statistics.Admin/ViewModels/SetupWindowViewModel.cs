using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using Telenec.Mail.Statistics.Admin.Services;

namespace Telenec.Mail.Statistics.Admin.ViewModels;

public sealed partial class SetupWindowViewModel : ObservableObject
{
    private readonly IAdminConnectionSetupService _setupService;

    public SetupWindowViewModel(
        IAdminConnectionSetupService setupService)
    {
        _setupService = setupService;
    }

    public string WindowTitle =>
        "Telenec Mail Statistik einrichten";

    [ObservableProperty]
    private string serverAddress = string.Empty;

    [ObservableProperty]
    private string statusText =
        "Bitte Serveradresse und Admin-Schlüssel eingeben.";

    [ObservableProperty]
    private string statusKind = "Info";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    private bool isBusy;

    public bool CanSubmit =>
        !IsBusy;

    public async Task<bool> ConfigureAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        StatusKind = "Loading";
        StatusText =
            "Verbindung zum Statistikserver wird geprüft …";

        try
        {
            await _setupService.ConfigureAsync(
                ServerAddress,
                apiKey,
                cancellationToken);

            StatusKind = "Success";
            StatusText =
                "Verbindung erfolgreich. Die Einrichtung wurde gespeichert.";

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            StatusKind = "Error";
            StatusText =
                "Der Admin-API-Schlüssel wurde vom Server abgelehnt.";
        }
        catch (HttpRequestException)
        {
            StatusKind = "Error";
            StatusText =
                "Der Statistikserver ist unter dieser Adresse nicht erreichbar.";
        }
        catch (TaskCanceledException)
        {
            StatusKind = "Warning";
            StatusText =
                "Die Verbindung zum Statistikserver hat zu lange gedauert.";
        }
        catch (InvalidOperationException exception)
        {
            StatusKind = "Error";
            StatusText =
                exception.Message;
        }
        catch (Exception)
        {
            StatusKind = "Error";
            StatusText =
                "Die Einrichtung konnte nicht abgeschlossen werden.";
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }
}