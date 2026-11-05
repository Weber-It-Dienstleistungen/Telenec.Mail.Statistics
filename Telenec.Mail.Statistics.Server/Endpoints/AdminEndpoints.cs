
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
         * Neuer Reputation-Endpoint.
         *
         * Liefert ausschließlich den zuletzt
         * gespeicherten Monitoring-Zustand.
         *
         * Ein API-Aufruf startet keine zusätzliche
         * DNS- oder Blacklist-Abfrage.
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
    /// Es werden keine externen Abfragen ausgeführt.
    ///
    /// Der Snapshot enthält:
    /// - konfigurierten Hostnamen
    /// - erwartete Versand-IP
    /// - zuletzt aufgelöste IPv4-Adressen
    /// - letzten Prüfversuch
    /// - letzten erfolgreichen Prüfzeitpunkt
    /// - Ergebnis der PSBL-Prüfung
    ///
    /// Der Endpoint ist ausschließlich lesend.
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

        return Results.Ok(snapshot);
    }
}
