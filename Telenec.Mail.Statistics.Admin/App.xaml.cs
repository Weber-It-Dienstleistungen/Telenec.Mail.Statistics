using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telenec.Mail.Statistics.Admin.Configuration;
using Telenec.Mail.Statistics.Admin.Security;
using Telenec.Mail.Statistics.Admin.Services;
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

                services.AddHttpClient<
                    IAdminStatisticsClient,
                    AdminStatisticsClient>(
                    httpClient =>
                    {
                        httpClient.Timeout =
                            TimeSpan.FromSeconds(10);
                    });

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        _host.Start();

        var mainWindow =
            _host.Services.GetRequiredService<MainWindow>();

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