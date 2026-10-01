using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Telenec.Mail.Statistics.Admin.Models;

namespace Telenec.Mail.Statistics.Admin.Services;

public sealed class AdminStatisticsClient : IAdminStatisticsClient
{
    private const string BaseUrlConfigurationKey = "AdminApi:BaseUrl";
    private const string ApiKeyConfigurationKey = "AdminApi:ApiKey";
    private const string AdminApiKeyHeaderName = "X-Admin-Key";

    private readonly HttpClient _httpClient;
    private readonly Uri _dashboardUri;
    private readonly string _apiKey;

    public AdminStatisticsClient(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;

        var configuredBaseUrl =
            configuration[BaseUrlConfigurationKey];

        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            throw new InvalidOperationException(
                $"Die Admin-API-Adresse '{BaseUrlConfigurationKey}' wurde nicht konfiguriert.");
        }

        if (!Uri.TryCreate(
                configuredBaseUrl,
                UriKind.Absolute,
                out var baseUri))
        {
            throw new InvalidOperationException(
                $"Die Admin-API-Adresse '{BaseUrlConfigurationKey}' ist ungültig.");
        }

        var isHttps =
            string.Equals(
                baseUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);

        var isDevelopmentLoopback =
            string.Equals(
                baseUri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase)
            && baseUri.IsLoopback;

        if (!isHttps && !isDevelopmentLoopback)
        {
            throw new InvalidOperationException(
                "Die Admin-API muss über HTTPS erreichbar sein. " +
                "HTTP ist ausschließlich für lokale Entwicklungsadressen erlaubt.");
        }

        var normalizedBaseUri =
            baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? baseUri
                : new Uri(
                    baseUri.AbsoluteUri + "/",
                    UriKind.Absolute);

        _dashboardUri =
            new Uri(
                normalizedBaseUri,
                "api/v1/admin/dashboard");

        var configuredApiKey =
            configuration[ApiKeyConfigurationKey];

        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            throw new InvalidOperationException(
                $"Der Admin-API-Schlüssel '{ApiKeyConfigurationKey}' wurde nicht konfiguriert.");
        }

        byte[] decodedApiKey;

        try
        {
            decodedApiKey =
                Convert.FromBase64String(configuredApiKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"Der Admin-API-Schlüssel '{ApiKeyConfigurationKey}' ist kein gültiger Base64-Wert.",
                exception);
        }

        if (decodedApiKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"Der Admin-API-Schlüssel '{ApiKeyConfigurationKey}' muss mindestens 32 Bytes lang sein.");
        }

        _apiKey = configuredApiKey;
    }

    public async Task<DashboardStatisticsResponse> GetDashboardStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                _dashboardUri);

        request.Headers.TryAddWithoutValidation(
            AdminApiKeyHeaderName,
            _apiKey);

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

        return statistics
            ?? throw new InvalidDataException(
                "Der Statistikserver hat keine gültigen Dashboard-Daten geliefert.");
    }
}