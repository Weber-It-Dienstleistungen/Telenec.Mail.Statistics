using Telenec.Mail.Statistics.Server.Validation;

namespace Telenec.Mail.Statistics.Server.Services;

public interface IUsageStatisticsService
{
    Task RecordHeartbeatAsync(
        ValidatedHeartbeatRequest request,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(
        ValidatedRevokeRequest request,
        CancellationToken cancellationToken = default);
}