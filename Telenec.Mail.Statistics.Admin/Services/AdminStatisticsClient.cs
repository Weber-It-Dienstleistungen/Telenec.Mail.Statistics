using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Telenec.Mail.Statistics.Admin.Configuration;
using Telenec.Mail.Statistics.Admin.Models;
using Telenec.Mail.Statistics.Admin.Security;

namespace Telenec.Mail.Statistics.Admin.Services;

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
}