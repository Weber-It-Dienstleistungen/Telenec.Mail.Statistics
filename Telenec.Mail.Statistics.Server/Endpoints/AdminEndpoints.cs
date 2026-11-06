
using Telenec.Mail.Statistics.Server.Reputation;
using Telenec.Mail.Statistics.Server.Security;
using Telenec.Mail.Statistics.Server.Services;

namespace Telenec.Mail.Statistics.Server.Endpoints;

public static class AdminEndpoints
{
    private const string AdminApiKeyHeaderName =
        "X-Admin-Key";

    public static IEndpointRouteBuilder MapAdminEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        /*
         * Bestehender Statistik-Endpoint.
         *
         * Die vorhandene Funktion bleibt unverändert.
         */
        endpoints.MapGet(
            "/api/v1/admin/dashboard",
            HandleDashboardAsync);

        /*
         * Geschützter Reputation-Endpoint.
         *
         * Gibt den zuletzt gespeicherten
         * Monitoring-Zustand zurück.
         *
         * Ein API-Aufruf löst keine zusätzliche
         * DNS- oder Blacklist-Abfrage aus.
         */
        endpoints.MapGet(
            "/api/v1/admin/reputation",
            HandleReputation);

        return endpoints;
    }

    /// <summary>
    /// Bestehende Nutzungsstatistik.
    ///
    /// Der Admin-API-Schlüssel wird vor dem Zugriff
    /// auf die Statistikdaten geprüft.
    /// </summary>
    private static async Task<IResult> HandleDashboardAsync(
        HttpRequest request,
        IAdminApiKeyValidator apiKeyValidator,
        IStatisticsQueryService statisticsQueryService,
        CancellationToken cancellationToken)
    {
        var providedApiKey =
            request.Headers[AdminApiKeyHeaderName]
                .FirstOrDefault();

        if (!apiKeyValidator.IsValid(providedApiKey))
        {
            return Results.Unauthorized();
        }

        var statistics =
            await statisticsQueryService
                .GetDashboardStatisticsAsync(
                    cancellationToken);

        return Results.Ok(statistics);
    }

    /// <summary>
    /// Liefert den aktuellen Blacklist-Monitoring-Zustand.
    ///
    /// Der Zugriff ist durch denselben Admin-API-Schlüssel
    /// geschützt wie die bestehende Nutzungsstatistik.
    ///
    /// Der Endpoint ist ausschließlich lesend.
    ///
    /// Der Blacklist-Status wird ausdrücklich als
    /// sprechender Zeichenfolgenwert zurückgegeben.
    ///
    /// Dadurch ist die API nicht von den numerischen
    /// Enum-Werten des Servers abhängig.
    /// </summary>
    private static IResult HandleReputation(
        HttpRequest request,
        IAdminApiKeyValidator apiKeyValidator,
        BlacklistMonitoringService monitoringService)
    {
        var providedApiKey =
            request.Headers[AdminApiKeyHeaderName]
                .FirstOrDefault();

        if (!apiKeyValidator.IsValid(providedApiKey))
        {
            return Results.Unauthorized();
        }

        var snapshot =
            monitoringService.GetSnapshot();

        /*
         * Expliziter API-Vertrag.
         *
         * Die bestehende Snapshot-Struktur
         * wird beibehalten.
         *
         * Lediglich der Status wird von einem
         * numerischen Enum in einen stabilen
         * Namen umgewandelt.
         *
         * Die übrigen Felder behalten ihre
         * bisherigen Datentypen.
         */
        var response = new
        {
            snapshot.Hostname,

            snapshot.ExpectedIpv4,

            snapshot.ResolvedIpv4Addresses,

            snapshot.LastAttemptUtc,

            snapshot.LastSuccessfulCheckUtc,

            Result = new
            {
                snapshot.Result.BlacklistId,

                snapshot.Result.CheckedTarget,

                Status =
                    snapshot.Result.Status.ToString(),

                snapshot.Result.StartedAtUtc,

                snapshot.Result.CompletedAtUtc,

                snapshot.Result.ProviderResponse,

                snapshot.Result.ErrorMessage,

                snapshot.Result.IsConclusive,

                snapshot.Result.IsListed
            }
        };

        return Results.Ok(response);
    }
}
