using Microsoft.EntityFrameworkCore;
using Telenec.Mail.Statistics.Server.Data;

namespace Telenec.Mail.Statistics.Server.Services;

public sealed class StatisticsQueryService : IStatisticsQueryService
{
    private readonly StatisticsDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public StatisticsQueryService(
        StatisticsDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<DashboardStatistics> GetDashboardStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var todayUtc = nowUtc.Date;
        var sevenDaysAgoUtc = nowUtc.AddDays(-7);
        var thirtyDaysAgoUtc = nowUtc.AddDays(-30);

        var statistics = await _dbContext.Installations
            .GroupBy(_ => 1)
            .Select(group => new DashboardStatistics(
                group.Count(),
                group.Count(x => x.LastSeenUtc >= todayUtc),
                group.Count(x => x.LastSeenUtc >= sevenDaysAgoUtc),
                group.Count(x => x.LastSeenUtc >= thirtyDaysAgoUtc),
                group.Count(x => x.LastSeenUtc < thirtyDaysAgoUtc)))
            .SingleOrDefaultAsync(cancellationToken);

        return statistics
            ?? new DashboardStatistics(
                TotalInstallations: 0,
                ActiveToday: 0,
                ActiveLast7Days: 0,
                ActiveLast30Days: 0,
                InactiveOver30Days: 0);
    }
}