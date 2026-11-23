using Telenec.Mail.Statistics.Server.Data;
using Telenec.Mail.Statistics.Server.MailSecurity;
using Telenec.Mail.Statistics.Server.Security;

namespace Telenec.Mail.Statistics.Server.Services;

public sealed class ServerInitializationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IInstallationKeyService _installationKeyService;
    private readonly IAdminApiKeyValidator _adminApiKeyValidator;
    private readonly ILogger<ServerInitializationHostedService> _logger;
    private readonly IConfiguration _configuration;

    public ServerInitializationHostedService(
        IServiceScopeFactory scopeFactory,
        IInstallationKeyService installationKeyService,
        IAdminApiKeyValidator adminApiKeyValidator,
        IConfiguration configuration,
        ILogger<ServerInitializationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _installationKeyService = installationKeyService;
        _adminApiKeyValidator = adminApiKeyValidator;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _installationKeyService;
        _ = _adminApiKeyValidator;

        _logger.LogInformation(
            "Initialisiere Statistikdatenbank mit ausdruecklicher Migrationsfreigabe.");

        await using var scope = _scopeFactory.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider.GetRequiredService<StatisticsDbContext>();

        await MailSecuritySchemaMigrationPolicy.MigrateAsync(
            dbContext, _configuration, cancellationToken);

        _logger.LogInformation(
            "Statistikdatenbank wurde gemaess Migrationsfreigabe initialisiert.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}