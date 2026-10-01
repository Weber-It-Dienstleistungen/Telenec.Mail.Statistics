using Microsoft.AspNetCore.Mvc;
using Telenec.Mail.Statistics.Server.Contracts;
using Telenec.Mail.Statistics.Server.Services;
using Telenec.Mail.Statistics.Server.Validation;

namespace Telenec.Mail.Statistics.Server.Endpoints;

public static class UsageEndpoints
{
    public const string RateLimitPolicyName = "UsageApi";

    private const long MaximumRequestBodySize = 2 * 1024;

    public static IEndpointRouteBuilder MapUsageEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/usage")
            .RequireRateLimiting(RateLimitPolicyName);

        group.MapPost(
                "/heartbeat",
                HandleHeartbeatAsync)
            .WithMetadata(
                new RequestSizeLimitAttribute(MaximumRequestBodySize));

        group.MapPost(
                "/revoke",
                HandleRevokeAsync)
            .WithMetadata(
                new RequestSizeLimitAttribute(MaximumRequestBodySize));

        return endpoints;
    }

    private static async Task<IResult> HandleHeartbeatAsync(
        HeartbeatRequest request,
        IUsageRequestValidator validator,
        IUsageStatisticsService service,
        CancellationToken cancellationToken)
    {
        if (!validator.TryValidate(
                request,
                out var validatedRequest,
                out var error))
        {
            return Results.BadRequest(new
            {
                error
            });
        }

        await service.RecordHeartbeatAsync(
            validatedRequest!,
            cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> HandleRevokeAsync(
        RevokeRequest request,
        IUsageRequestValidator validator,
        IUsageStatisticsService service,
        CancellationToken cancellationToken)
    {
        if (!validator.TryValidate(
                request,
                out var validatedRequest,
                out var error))
        {
            return Results.BadRequest(new
            {
                error
            });
        }

        await service.RevokeAsync(
            validatedRequest!,
            cancellationToken);

        return Results.NoContent();
    }
}