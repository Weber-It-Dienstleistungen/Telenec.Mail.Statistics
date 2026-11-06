
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
         * Liefert den aktuellen Monitoring-Snapshot
         * sowie die vollständige Blacklist-Übersicht.
         *
         * Der API-Aufruf führt selbst keine DNS-
         * oder Blacklist-Abfragen durch.
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
    /// Die bisherige API-Antwort bleibt kompatibel.
    /// Zusätzlich wird eine vollständige Übersicht
    /// aus dem BlacklistCatalog bereitgestellt.
    ///
    /// Die Übersicht enthält derzeit:
    ///
    /// - 68 Referenzlisten
    /// - 1 integrierte Liste (PSBL)
    /// - 67 noch nicht integrierte Listen
    ///
    /// Nur tatsächlich vorhandene Monitoring-Ergebnisse
    /// werden als durchgeführte Prüfungen dargestellt.
    /// </summary>
    private static IResult HandleReputation(
        HttpRequest request,
        IAdminApiKeyValidator apiKeyValidator,
        BlacklistMonitoringService monitoringService)
    {
        /*
         * Authentifizierung erfolgt vor
         * dem Zugriff auf Monitoring-Daten.
         */
        var providedApiKey =
            request.Headers[AdminApiKeyHeaderName]
                .FirstOrDefault();

        if (!apiKeyValidator.IsValid(providedApiKey))
        {
            return Results.Unauthorized();
        }

        /*
         * Atomar veröffentlichten Snapshot lesen.
         *
         * Diese Operation führt keine externe
         * Blacklist-Abfrage aus.
         */
        var snapshot =
            monitoringService.GetSnapshot();

        /*
         * Aktuell steht das Ergebnis des
         * PSBL-Monitorings zur Verfügung.
         *
         * Die Dictionary-Struktur ermöglicht es,
         * künftig mehrere Prüfergebnisse an
         * die OverviewFactory zu übergeben.
         *
         * Die Erweiterung des Monitoring-Dienstes
         * erfolgt in einem späteren Sprint.
         */
        IReadOnlyDictionary<string, BlacklistCheckResult>
            latestResults =
                new Dictionary<string, BlacklistCheckResult>(
                    StringComparer.Ordinal)
                {
                    [snapshot.Result.BlacklistId] =
                        snapshot.Result
                };

        /*
         * Vollständige Blacklist-Übersicht
         * aus dem Referenzkatalog erzeugen.
         *
         * Nicht integrierte Einträge erhalten
         * ausschließlich den fachlichen
         * Status "NotIntegrated".
         */
        var overview =
            BlacklistOverviewFactory.Create(
                latestResults);

        /*
         * Rückwärtskompatibler API-Vertrag.
         *
         * Alle bisherigen Felder bleiben erhalten.
         * Insbesondere bleibt "result" weiterhin
         * das einzelne PSBL-Prüfergebnis.
         *
         * Das neue Feld "overview" ergänzt
         * die Antwort um die vollständige
         * dynamische Blacklist-Übersicht.
         *
         * Der WPF-Client kann damit später
         * beliebig viele Blacklist-Einträge
         * darstellen, ohne eine feste Liste
         * im Client hinterlegen zu müssen.
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
            },

            Overview = overview
        };

        return Results.Ok(response);
    }
}
