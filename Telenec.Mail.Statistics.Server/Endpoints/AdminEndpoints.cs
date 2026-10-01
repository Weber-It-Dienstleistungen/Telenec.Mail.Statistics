using Telenec.Mail.Statistics.Server.Security;
using Telenec.Mail.Statistics.Server.Services;

namespace Telenec.Mail.Statistics.Server.Endpoints;

public static class AdminEndpoints
{
    private const string AdminApiKeyHeaderName = "X-Admin-Key";

    public static IEndpointRouteBuilder MapAdminEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/v1/admin/dashboard",
            HandleDashboardAsync);

        return endpoints;
    }

    private static async Task<IResult> HandleDashboardAsync(
        HttpRequest request,
        IAdminApiKeyValidator apiKeyValidator,
        IStatisticsQueryService statisticsQueryService,
        CancellationToken cancellationToken)
    {
        var providedApiKey =
            request.Headers[AdminApiKeyHeaderName].FirstOrDefault();

        if (!apiKeyValidator.IsValid(providedApiKey))
        {
            return Results.Unauthorized();
        }

        var statistics =
            await statisticsQueryService.GetDashboardStatisticsAsync(
                cancellationToken);

        return Results.Ok(statistics);
    }
}