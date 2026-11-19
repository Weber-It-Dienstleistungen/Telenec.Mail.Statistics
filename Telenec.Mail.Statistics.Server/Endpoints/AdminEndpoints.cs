
using Telenec.Mail.Statistics.Server.MailTraffic;
using Telenec.Mail.Statistics.Server.MailSecurity;
using Telenec.Mail.Statistics.Server.Reputation;
using Telenec.Mail.Statistics.Server.Security;
using Telenec.Mail.Statistics.Server.Services;

namespace Telenec.Mail.Statistics.Server.Endpoints;

public static class AdminEndpoints
{
    public const string BlacklistCheckRateLimitPolicyName =
        "BlacklistManualCheck";

    private const string AdminApiKeyHeaderName =
        "X-Admin-Key";

    public static IEndpointRouteBuilder MapAdminEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        /*
         * Bestehende Nutzungsstatistik.
         * Unveränderte Schnittstelle.
         */
        endpoints.MapGet(
            "/api/v1/admin/dashboard",
            HandleDashboardAsync);

        /*
         * Liest ausschließlich den letzten
         * Blacklist-Prüfzustand.
         *
         * GET löst keine DNS-Abfragen aus.
         */
        endpoints.MapGet(
            "/api/v1/admin/reputation",
            HandleReputation);

        /*
         * Optionaler 24-Stunden-Mailverkehrsreport.
         * Nur aggregierte Zähler, ausschließlich mit Admin-Key.
         * Ohne Quelle wird Available=false statt falscher Nullwerte geliefert.
         */
        endpoints.MapGet(
            "/api/v1/admin/mail-traffic",
            HandleMailTrafficAsync);

        /*
         * Geschuetzte Plesk-Sicherheitsstatistiken.
         * Die API ist ausschliesslich lesend und fuehrt keine
         * Plesk-Kommandos oder Postfach-Aktionen aus.
         */
        endpoints.MapGet(
            "/api/v1/admin/mail-security",
            HandleMailSecurityAsync);

        /*
         * Geschützter manueller Prüfauslöser.
         *
         * Nur POST startet externe Abfragen.
         *
         * Zusätzlich gilt ein eigenes
         * Rate-Limit für diese Schnittstelle.
         */
        endpoints.MapPost(
                "/api/v1/admin/reputation/check",
                HandleReputationCheckAsync)
            .RequireRateLimiting(
                BlacklistCheckRateLimitPolicyName);

        return endpoints;
    }

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

    private static async Task<IResult> HandleMailTrafficAsync(
        HttpRequest request,
        IAdminApiKeyValidator apiKeyValidator,
        MailTrafficSnapshotReader reader,
        CancellationToken cancellationToken)
    {
        var providedApiKey =
            request.Headers[AdminApiKeyHeaderName].FirstOrDefault();

        if (!apiKeyValidator.IsValid(providedApiKey))
        {
            return Results.Unauthorized();
        }

        var data = await reader.ReadAsync(cancellationToken);
        return Results.Ok(data);
    }

    private static async Task<IResult> HandleMailSecurityAsync(
        HttpRequest request,
        IAdminApiKeyValidator apiKeyValidator,
        MailSecuritySnapshotReader reader,
        CancellationToken cancellationToken)
    {
        var providedApiKey =
            request.Headers[AdminApiKeyHeaderName].FirstOrDefault();

        if (!apiKeyValidator.IsValid(providedApiKey))
        {
            return Results.Unauthorized();
        }

        var data = await reader.ReadAsync(cancellationToken);
        return Results.Ok(data);
    }

    /// <summary>
    /// Rückwärtskompatible Lese-API.
    ///
    /// Die Antwort enthält PSBL als bisheriges
    /// Einzelresultat sowie die vollständige
    /// dynamische Blacklist-Übersicht.
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

        var overview =
            BlacklistOverviewFactory.Create(
                snapshot.Results);

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
            },

            Overview = overview
        };

        return Results.Ok(response);
    }

    /// <summary>
    /// Startet eine neue Blacklist-Prüfung
    /// nach erfolgreicher Authentifizierung.
    ///
    /// 204 = Prüfdurchlauf wurde abgeschlossen.
    /// 401 = ungültiger Admin-Schlüssel.
    /// 409 = ein Prüfdurchlauf läuft bereits.
    /// 429 = Rate-Limit überschritten.
    ///
    /// 204 bedeutet nicht, dass alle Anbieter
    /// ein eindeutiges Ergebnis geliefert haben.
    /// Die Ergebnisse werden über GET gelesen.
    /// </summary>
    private static async Task<IResult>
        HandleReputationCheckAsync(
            HttpRequest request,
            IAdminApiKeyValidator apiKeyValidator,
            BlacklistMonitoringService monitoringService,
            CancellationToken cancellationToken)
    {
        var providedApiKey =
            request.Headers[AdminApiKeyHeaderName]
                .FirstOrDefault();

        if (!apiKeyValidator.IsValid(providedApiKey))
        {
            return Results.Unauthorized();
        }

        var wasExecuted =
            await monitoringService.CheckNowAsync(
                cancellationToken);

        if (!wasExecuted)
        {
            return Results.Conflict(new
            {
                error =
                    "Eine Blacklist-Prüfung läuft bereits."
            });
        }

        return Results.NoContent();
    }
}
