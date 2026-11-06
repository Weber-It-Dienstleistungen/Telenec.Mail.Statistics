
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
    /// Zusätzlich wird die vollständige Übersicht
    /// aus dem BlacklistCatalog bereitgestellt.
    ///
    /// Die Übersicht enthält derzeit:
    ///
    /// - 68 Referenzlisten
    /// - 1 integrierte Liste (PSBL)
    /// - 67 noch nicht integrierte Listen
    ///
    /// Die Kennzahlen werden aus den tatsächlich
    /// vorhandenen Monitoring-Ergebnissen berechnet.
    ///
    /// Spätere Erweiterungen um weitere Anbieter
    /// benötigen keine Änderung des API-Vertrags.
    /// </summary>
    private static IResult HandleReputation(
        HttpRequest request,
        IAdminApiKeyValidator apiKeyValidator,
        BlacklistMonitoringService monitoringService)
    {
        /*
         * Authentifizierung vor dem Zugriff
         * auf die Monitoring-Daten.
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
         * Der Snapshot enthält jetzt die
         * vollständige Ergebnissammlung.
         *
         * Der API-Aufruf löst keine zusätzlichen
         * externen Prüfungen aus.
         */
        var snapshot =
            monitoringService.GetSnapshot();

        /*
         * Dynamische Blacklist-Übersicht.
         *
         * Die Ergebnissammlung kommt unmittelbar
         * aus dem Monitoring-Snapshot.
         *
         * Es wird kein zusätzliches Dictionary
         * mit nur einem PSBL-Ergebnis erzeugt.
         *
         * Dadurch können später mehrere
         * Blacklist-Ergebnisse gleichzeitig
         * in der Übersicht erscheinen.
         */
        var overview =
            BlacklistOverviewFactory.Create(
                snapshot.Results);

        /*
         * Rückwärtskompatibler API-Vertrag.
         *
         * Alle bisherigen Felder bleiben erhalten.
         *
         * Das Feld "result" liefert weiterhin
         * das bestehende PSBL-Einzelprüfergebnis.
         *
         * Das Feld "overview" enthält die
         * vollständige dynamische Übersicht
         * aller Referenzlisten und der jeweils
         * vorhandenen Monitoring-Ergebnisse.
         *
         * Die tatsächlichen Prüfergebnisse
         * stammen ausschließlich aus dem
         * serverseitigen Monitoring-Dienst.
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
