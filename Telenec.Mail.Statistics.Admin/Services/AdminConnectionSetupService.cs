using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Telenec.Mail.Statistics.Admin.Configuration;
using Telenec.Mail.Statistics.Admin.Models;
using Telenec.Mail.Statistics.Admin.Security;

namespace Telenec.Mail.Statistics.Admin.Services;

public sealed class AdminConnectionSetupService :
    IAdminConnectionSetupService
{
    private const string AdminApiKeyHeaderName =
        "X-Admin-Key";

    private readonly HttpClient _httpClient;
    private readonly IAdminApiBaseUrlStore _baseUrlStore;
    private readonly IAdminApiKeyStore _apiKeyStore;

    public AdminConnectionSetupService(
        HttpClient httpClient,
        IAdminApiBaseUrlStore baseUrlStore,
        IAdminApiKeyStore apiKeyStore)
    {
        _httpClient = httpClient;
        _baseUrlStore = baseUrlStore;
        _apiKeyStore = apiKeyStore;
    }

    public async Task ConfigureAsync(
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var baseUri =
            ValidateAndNormalizeBaseUri(baseUrl);

        ValidateApiKey(apiKey);

        var dashboardUri =
            new Uri(
                baseUri,
                "api/v1/admin/dashboard");

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

        await _baseUrlStore.SaveAsync(
            baseUri.AbsoluteUri,
            cancellationToken);

        await _apiKeyStore.SaveAsync(
            apiKey,
            cancellationToken);
    }

    private static Uri ValidateAndNormalizeBaseUri(
        string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "Die Serveradresse darf nicht leer sein.");
        }

        if (!Uri.TryCreate(
                baseUrl.Trim(),
                UriKind.Absolute,
                out var baseUri))
        {
            throw new InvalidOperationException(
                "Die Serveradresse ist ungültig.");
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

        var absoluteUri =
            baseUri.AbsoluteUri.EndsWith(
                "/",
                StringComparison.Ordinal)
                ? baseUri.AbsoluteUri
                : baseUri.AbsoluteUri + "/";

        return new Uri(
            absoluteUri,
            UriKind.Absolute);
    }

    private static void ValidateApiKey(
        string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Der Admin-API-Schlüssel darf nicht leer sein.");
        }

        byte[] decodedApiKey;

        try
        {
            decodedApiKey =
                Convert.FromBase64String(apiKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Der Admin-API-Schlüssel ist kein gültiger Base64-Wert.",
                exception);
        }

        if (decodedApiKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Der Admin-API-Schlüssel muss mindestens 32 Bytes lang sein.");
        }
    }
}