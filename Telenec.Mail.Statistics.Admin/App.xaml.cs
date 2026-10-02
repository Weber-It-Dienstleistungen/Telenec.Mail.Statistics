using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telenec.Mail.Statistics.Admin.Configuration;
using Telenec.Mail.Statistics.Admin.Security;
using Telenec.Mail.Statistics.Admin.Services;
using Telenec.Mail.Statistics.Admin.Services.Updates;
using Telenec.Mail.Statistics.Admin.ViewModels;

namespace Telenec.Mail.Statistics.Admin;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host
            .CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<
                    IAdminApiKeyStore,
                    WindowsAdminApiKeyStore>();

                services.AddSingleton<
                    IAdminApiKeyProvider,
                    AdminApiKeyProvider>();

                services.AddSingleton<
                    IAdminApiBaseUrlStore,
                    JsonAdminApiBaseUrlStore>();

                services.AddSingleton<
                    IAdminApiBaseUrlProvider,
                    AdminApiBaseUrlProvider>();

                services.AddSingleton<
                    IApplicationUpdateService,
                    VelopackApplicationUpdateService>();

                services.AddHttpClient<
                    IAdminStatisticsClient,
                    AdminStatisticsClient>(
                    httpClient =>
                    {
                        httpClient.Timeout =
                            TimeSpan.FromSeconds(10);
                    });

                services.AddHttpClient<
                    IAdminConnectionSetupService,
                    AdminConnectionSetupService>(
                    httpClient =>
                    {
                        httpClient.Timeout =
                            TimeSpan.FromSeconds(10);
                    });

                services.AddTransient<SetupWindowViewModel>();
                services.AddTransient<SetupWindow>();

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode =
            ShutdownMode.OnExplicitShutdown;

        await _host.StartAsync();

        var baseUrlStore =
            _host.Services
                .GetRequiredService<IAdminApiBaseUrlStore>();

        var apiKeyStore =
            _host.Services
                .GetRequiredService<IAdminApiKeyStore>();

        var storedBaseUrl =
            await baseUrlStore.ReadAsync();

        var storedApiKey =
            await apiKeyStore.ReadAsync();

        var isConfigured =
            !string.IsNullOrWhiteSpace(storedBaseUrl)
            && !string.IsNullOrWhiteSpace(storedApiKey);

        if (!isConfigured)
        {
            var setupWindow =
                _host.Services
                    .GetRequiredService<SetupWindow>();

            var setupResult =
                setupWindow.ShowDialog();

            if (setupResult != true)
            {
                Shutdown();
                return;
            }
        }

        var mainWindow =
            _host.Services
                .GetRequiredService<MainWindow>();

        MainWindow = mainWindow;

        ShutdownMode =
            ShutdownMode.OnMainWindowClose;

        mainWindow.Show();
    }

    protected override void OnExit(
        ExitEventArgs e)
    {
        _host.StopAsync()
            .GetAwaiter()
            .GetResult();

        _host.Dispose();

        base.OnExit(e);
    }
}