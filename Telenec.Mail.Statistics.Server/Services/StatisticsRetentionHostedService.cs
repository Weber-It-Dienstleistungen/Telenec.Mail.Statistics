using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telenec.Mail.Statistics.Server.Data;
using Telenec.Mail.Statistics.Server.Options;

namespace Telenec.Mail.Statistics.Server.Services;

public sealed class StatisticsRetentionHostedService :
    BackgroundService
{
    private static readonly TimeSpan CleanupInterval =
        TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory
        _scopeFactory;

    private readonly TimeProvider
        _timeProvider;

    private readonly StatisticsRetentionOptions
        _options;

    private readonly ILogger<StatisticsRetentionHostedService>
        _logger;

    public StatisticsRetentionHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<StatisticsRetentionOptions> options,
        ILogger<StatisticsRetentionHostedService> logger)
    {
        _scopeFactory =
            scopeFactory;

        _timeProvider =
            timeProvider;

        _options =
            options.Value;

        _logger =
            logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        /*
         * Direkt nach dem Serverstart wird einmal bereinigt.
         *
         * Dadurch ist die Löschfrist nicht davon abhängig,
         * dass der Dienst 24 Stunden ohne Unterbrechung läuft.
         */
        await CleanupExpiredInstallationsAsync(
            stoppingToken);

        using var timer =
            new PeriodicTimer(
                CleanupInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(
                       stoppingToken))
            {
                await CleanupExpiredInstallationsAsync(
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            /*
             * Normales Beenden des Serverdienstes.
             */
        }
    }

    private async Task CleanupExpiredInstallationsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var nowUtc =
                _timeProvider
                    .GetUtcNow()
                    .UtcDateTime;

            var cutoffUtc =
                nowUtc.AddDays(
                    -_options.RetentionDays);

            await using var scope =
                _scopeFactory
                    .CreateAsyncScope();

            var dbContext =
                scope.ServiceProvider
                    .GetRequiredService<
                        StatisticsDbContext>();

            /*
             * Entscheidend ist die letzte Aktivität.
             *
             * Eine Installation bleibt damit erhalten,
             * solange weiterhin Heartbeats eingehen.
             * Erst nach vollständiger Inaktivität über die
             * konfigurierte Frist wird sie gelöscht.
             */
            var deletedCount =
                await dbContext.Installations
                    .Where(
                        installation =>
                            installation.LastSeenUtc <
                            cutoffUtc)
                    .ExecuteDeleteAsync(
                        cancellationToken);

            if (deletedCount > 0)
            {
                _logger.LogInformation(
                    "Automatische Statistikbereinigung hat {DeletedCount} inaktive Installation(en) gelöscht.",
                    deletedCount);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            /*
             * Die Bereinigung ist wichtig, darf aber niemals
             * den laufenden Statistikdienst beenden.
             *
             * Beim nächsten täglichen Durchlauf wird erneut
             * versucht zu bereinigen.
             */
            _logger.LogError(
                exception,
                "Automatische Statistikbereinigung ist fehlgeschlagen.");
        }
    }
}