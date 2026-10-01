using Telenec.Mail.Statistics.Admin.Models;

namespace Telenec.Mail.Statistics.Admin.Services;

public interface IAdminStatisticsClient
{
    Task<DashboardStatisticsResponse> GetDashboardStatisticsAsync(
        CancellationToken cancellationToken = default);
}