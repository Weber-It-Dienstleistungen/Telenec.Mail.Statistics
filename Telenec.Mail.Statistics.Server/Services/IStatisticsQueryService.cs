namespace Telenec.Mail.Statistics.Server.Services;

public interface IStatisticsQueryService
{
    Task<DashboardStatistics> GetDashboardStatisticsAsync(
        CancellationToken cancellationToken = default);
}