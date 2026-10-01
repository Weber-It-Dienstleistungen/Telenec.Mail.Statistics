using Microsoft.Extensions.Configuration;

namespace Telenec.Mail.Statistics.Admin.Configuration;

public sealed class AdminApiBaseUrlProvider :
    IAdminApiBaseUrlProvider
{
    private const string BaseUrlConfigurationKey =
        "AdminApi:BaseUrl";

    private readonly IAdminApiBaseUrlStore _baseUrlStore;
    private readonly IConfiguration _configuration;

    public AdminApiBaseUrlProvider(
        IAdminApiBaseUrlStore baseUrlStore,
        IConfiguration configuration)
    {
        _baseUrlStore = baseUrlStore;
        _configuration = configuration;
    }

    public async Task<Uri> GetBaseUriAsync(
        CancellationToken cancellationToken = default)
    {
        var storedBaseUrl =
            await _baseUrlStore.ReadAsync(
                cancellationToken);

        if (!string.IsNullOrWhiteSpace(storedBaseUrl))
        {
            return ValidateAndNormalizeBaseUri(
                storedBaseUrl,
                "Die lokal gespeicherte Admin-API-Adresse");
        }

        var configuredBaseUrl =
            _configuration[BaseUrlConfigurationKey];

        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            throw new InvalidOperationException(
                "Es wurde keine Admin-API-Adresse gefunden. " +
                $"Weder die lokale Konfiguration noch '{BaseUrlConfigurationKey}' enthalten eine Adresse.");
        }

        return ValidateAndNormalizeBaseUri(
            configuredBaseUrl,
            $"Die Admin-API-Adresse '{BaseUrlConfigurationKey}'");
    }

    public async Task ConfirmBaseUriAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        var validatedBaseUri =
            ValidateAndNormalizeBaseUri(
                baseUri.AbsoluteUri,
                "Die erfolgreich verwendete Admin-API-Adresse");

        var storedBaseUrl =
            await _baseUrlStore.ReadAsync(
                cancellationToken);

        if (!string.IsNullOrWhiteSpace(storedBaseUrl))
        {
            return;
        }

        await _baseUrlStore.SaveAsync(
            validatedBaseUri.AbsoluteUri,
            cancellationToken);
    }

    private static Uri ValidateAndNormalizeBaseUri(
        string baseUrl,
        string description)
    {
        if (!Uri.TryCreate(
                baseUrl,
                UriKind.Absolute,
                out var baseUri))
        {
            throw new InvalidOperationException(
                $"{description} ist ungültig.");
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
}