using Microsoft.EntityFrameworkCore;
using Telenec.Mail.Statistics.Server.Data;
using Telenec.Mail.Statistics.Server.Security;

namespace Telenec.Mail.Statistics.Server.Services;

public sealed class ServerInitializationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IInstallationKeyService _installationKeyService;
    private readonly ILogger<ServerInitializationHostedService> _logger;

    public ServerInitializationHostedService(
        IServiceScopeFactory scopeFactory,
        IInstallationKeyService installationKeyService,
        ILogger<ServerInitializationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _installationKeyService = installationKeyService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _installationKeyService;

        _logger.LogInformation(
            "Initialisiere Statistikdatenbank und wende ausstehende Migrationen an.");

        await using var scope = _scopeFactory.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);

        _logger.LogInformation(
            "Statistikdatenbank wurde erfolgreich initialisiert.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}