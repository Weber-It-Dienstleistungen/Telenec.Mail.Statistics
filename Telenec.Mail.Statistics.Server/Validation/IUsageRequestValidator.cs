using Telenec.Mail.Statistics.Server.Contracts;

namespace Telenec.Mail.Statistics.Server.Validation;

public interface IUsageRequestValidator
{
    bool TryValidate(
        HeartbeatRequest? request,
        out ValidatedHeartbeatRequest? validatedRequest,
        out string? error);

    bool TryValidate(
        RevokeRequest? request,
        out ValidatedRevokeRequest? validatedRequest,
        out string? error);
}