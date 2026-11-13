
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Telenec.Mail.Statistics.Admin.Configuration;
using Telenec.Mail.Statistics.Admin.Models;
using Telenec.Mail.Statistics.Admin.Security;

namespace Telenec.Mail.Statistics.Admin.Services;

/// <summary>
/// HTTP-Client für die geschützten
/// Administrationsschnittstellen des Statistikservers.
///
/// Der Client verwendet die bestehende
/// Serverkonfiguration und Admin-Schlüsselverwaltung.
/// </summary>
public sealed class AdminStatisticsClient : IAdminStatisticsClient
{
    private const string AdminApiKeyHeaderName =
        "X-Admin-Key";

    private readonly HttpClient _httpClient;
    private readonly IAdminApiKeyProvider _apiKeyProvider;
    private readonly IAdminApiBaseUrlProvider _baseUrlProvider;

    public AdminStatisticsClient(
        HttpClient httpClient,
        IAdminApiKeyProvider apiKeyProvider,
        IAdminApiBaseUrlProvider baseUrlProvider)
    {
        _httpClient = httpClient;
        _apiKeyProvider = apiKeyProvider;
        _baseUrlProvider = baseUrlProvider;
    }

    /// <summary>
    /// Ruft die bestehende Nutzungsstatistik ab.
    ///
    /// Die bisherige Funktionsweise bleibt unverändert.
    /// </summary>
    public async Task<DashboardStatisticsResponse> GetDashboardStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var baseUri =
            await _baseUrlProvider.GetBaseUriAsync(
                cancellationToken);

        var dashboardUri =
            new Uri(
                baseUri,
                "api/v1/admin/dashboard");

        var apiKey =
            await _apiKeyProvider.GetApiKeyAsync(
                cancellationToken);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                dashboardUri);

        request.Headers.TryAddWithoutValidation(
            AdminApiKeyHeaderName,
            apiKey);

        using var response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException(
                "Der Admin-API-Schlüssel wurde vom Server abgelehnt.");
        }

        response.EnsureSuccessStatusCode();

        var statistics =
            await response.Content
                .ReadFromJsonAsync<DashboardStatisticsResponse>(
                    cancellationToken: cancellationToken);

        if (statistics is null)
        {
            throw new InvalidDataException(
                "Der Statistikserver hat keine gültigen Dashboard-Daten geliefert.");
        }

        await _apiKeyProvider.ConfirmApiKeyAsync(
            apiKey,
            cancellationToken);

        await _baseUrlProvider.ConfirmBaseUriAsync(
            baseUri,
            cancellationToken);

        return statistics;
    }

    /// <summary>
    /// Ruft die aktuellen Reputationsdaten
    /// vom Statistikserver ab.
    ///
    /// API:
    /// GET /api/v1/admin/reputation
    ///
    /// Die eigentlichen DNSBL-Prüfungen
    /// finden ausschließlich serverseitig statt.
    ///
    /// Die WPF-Anwendung liest nur den
    /// zuletzt gespeicherten Monitoring-Snapshot.
    /// </summary>
    public async Task<ReputationMonitoringResponse> GetReputationAsync(
        CancellationToken cancellationToken = default)
    {
        /*
         * Die bestehende Serverkonfiguration
         * wird auch für die Reputation verwendet.
         *
         * Es werden keine zusätzlichen
         * Zugangsdaten benötigt.
         */

        var baseUri =
            await _baseUrlProvider.GetBaseUriAsync(
                cancellationToken);

        var reputationUri =
            new Uri(
                baseUri,
                "api/v1/admin/reputation");

        var apiKey =
            await _apiKeyProvider.GetApiKeyAsync(
                cancellationToken);

        /*
         * Geschützte HTTP-GET-Anfrage.
         *
         * Der vorhandene Admin-API-Schlüssel
         * wird im X-Admin-Key-Header übertragen.
         */

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                reputationUri);

        request.Headers.TryAddWithoutValidation(
            AdminApiKeyHeaderName,
            apiKey);

        using var response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        /*
         * Eine abgelehnte Authentifizierung
         * wird ausdrücklich unterschieden
         * von anderen HTTP-Fehlern.
         */

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException(
                "Der Admin-API-Schlüssel wurde vom Server abgelehnt.");
        }

        /*
         * Andere HTTP-Fehler werden an
         * die aufrufende Komponente weitergegeben.
         */

        response.EnsureSuccessStatusCode();

        /*
         * JSON-Antwort in unser WPF-Datenmodell
         * deserialisieren.
         */

        var reputation =
            await response.Content
                .ReadFromJsonAsync<ReputationMonitoringResponse>(
                    cancellationToken: cancellationToken);

        if (reputation is null)
        {
            throw new InvalidDataException(
                "Der Statistikserver hat keine gültigen Reputationsdaten geliefert.");
        }

        /*
         * Grundlegende Validierung.
         *
         * Unvollständige Antworten dürfen
         * nicht als erfolgreiches Monitoring
         * interpretiert werden.
         */

        if (string.IsNullOrWhiteSpace(reputation.Hostname)
            || string.IsNullOrWhiteSpace(reputation.ExpectedIpv4)
            || reputation.Result is null
            || string.IsNullOrWhiteSpace(reputation.Result.BlacklistId)
            || string.IsNullOrWhiteSpace(reputation.Result.Status))
        {
            throw new InvalidDataException(
                "Die Reputationsantwort des Statistikservers ist unvollständig.");
        }

        await _apiKeyProvider.ConfirmApiKeyAsync(
            apiKey,
            cancellationToken);

        await _baseUrlProvider.ConfirmBaseUriAsync(
            baseUri,
            cancellationToken);

        return reputation;
    }

    /// <summary>
    /// Startet eine manuelle Blacklist-Prüfung
    /// auf dem Statistikserver.
    ///
    /// API:
    /// POST /api/v1/admin/reputation/check
    ///
    /// Die WPF-Anwendung führt selbst
    /// keine DNSBL-Abfragen aus.
    ///
    /// HTTP 204 bedeutet:
    /// Der serverseitige Prüfdurchlauf wurde
    /// abgeschlossen.
    ///
    /// HTTP 204 bedeutet ausdrücklich nicht,
    /// dass sämtliche Anbieter ein eindeutiges
    /// Prüfergebnis geliefert haben.
    ///
    /// Die Ergebnisse werden anschließend
    /// separat mit GetReputationAsync geladen.
    /// </summary>
    public async Task TriggerReputationCheckAsync(
        CancellationToken cancellationToken = default)
    {
        /*
         * Die bestehende Serveradresse und
         * den vorhandenen Admin-Schlüssel
         * wiederverwenden.
         *
         * Es werden keine separaten
         * Blacklist-Zugangsdaten gespeichert.
         */

        var baseUri =
            await _baseUrlProvider.GetBaseUriAsync(
                cancellationToken);

        var checkUri =
            new Uri(
                baseUri,
                "api/v1/admin/reputation/check");

        var apiKey =
            await _apiKeyProvider.GetApiKeyAsync(
                cancellationToken);

        /*
         * Nur POST löst eine Prüfung aus.
         *
         * GET bleibt eine rein lesende
         * Statusabfrage.
         */

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                checkUri);

        request.Headers.TryAddWithoutValidation(
            AdminApiKeyHeaderName,
            apiKey);

        using var response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        /*
         * Fehlerzustände ausdrücklich
         * unterscheiden.
         *
         * So kann MiPos Oberfläche später
         * verständliche Meldungen anzeigen.
         */

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new UnauthorizedAccessException(
                "Der Admin-API-Schlüssel wurde vom Server abgelehnt.");
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException(
                "Auf dem Statistikserver läuft bereits eine Blacklist-Prüfung.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException(
                "Das Limit für manuelle Blacklist-Prüfungen wurde erreicht. Bitte später erneut versuchen.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound
            || response.StatusCode == HttpStatusCode.MethodNotAllowed)
        {
            throw new NotSupportedException(
                "Der Statistikserver unterstützt das manuelle Starten von Blacklist-Prüfungen noch nicht.");
        }

        /*
         * Sonstige HTTP-Fehler ausdrücklich
         * weiterreichen.
         *
         * Ein Serverfehler darf nicht
         * als abgeschlossene Prüfung gelten.
         */

        response.EnsureSuccessStatusCode();

        /*
         * Der aktuelle Serververtrag liefert
         * bei erfolgreicher Ausführung HTTP 204
         * ohne Antwortinhalt.
         *
         * Andere erfolgreiche Antworten gelten
         * hier nicht automatisch als Bestätigung
         * eines abgeschlossenen Prüfdurchlaufs.
         */

        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidDataException(
                "Der Statistikserver hat die Blacklist-Prüfung nicht mit HTTP 204 bestätigt.");
        }

        /*
         * Erst nach bestätigtem Abschluss
         * die verwendeten Zugangsdaten
         * und die Serveradresse bestätigen.
         */

        await _apiKeyProvider.ConfirmApiKeyAsync(
            apiKey,
            cancellationToken);

        await _baseUrlProvider.ConfirmBaseUriAsync(
            baseUri,
            cancellationToken);
    }
}
