using Microsoft.EntityFrameworkCore;
using Telenec.Mail.Statistics.Server.Data;
using Telenec.Mail.Statistics.Server.Security;
using Telenec.Mail.Statistics.Server.Validation;

namespace Telenec.Mail.Statistics.Server.Services;

public sealed class UsageStatisticsService : IUsageStatisticsService
{
    private readonly StatisticsDbContext _dbContext;
    private readonly IInstallationKeyService _installationKeyService;
    private readonly TimeProvider _timeProvider;

    public UsageStatisticsService(
        StatisticsDbContext dbContext,
        IInstallationKeyService installationKeyService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _installationKeyService = installationKeyService;
        _timeProvider = timeProvider;
    }

    public async Task RecordHeartbeatAsync(
        ValidatedHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        var installationKey =
            _installationKeyService.CreateInstallationKey(request.InstallationId);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO Installations
                 (InstallationKey, FirstSeenUtc, LastSeenUtc, CurrentVersion, ConsentVersion)
             VALUES
                 ({installationKey}, {nowUtc}, {nowUtc}, {request.ApplicationVersion}, {request.ConsentVersion})
             ON CONFLICT(InstallationKey) DO UPDATE SET
                 LastSeenUtc = excluded.LastSeenUtc,
                 CurrentVersion = excluded.CurrentVersion,
                 ConsentVersion = excluded.ConsentVersion;
             """,
            cancellationToken);
    }

    public async Task RevokeAsync(
        ValidatedRevokeRequest request,
        CancellationToken cancellationToken = default)
    {
        var installationKey =
            _installationKeyService.CreateInstallationKey(request.InstallationId);

        await _dbContext.Installations
            .Where(x => x.InstallationKey == installationKey)
            .ExecuteDeleteAsync(cancellationToken);
    }
}