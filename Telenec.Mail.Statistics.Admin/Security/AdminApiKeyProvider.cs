using Microsoft.Extensions.Configuration;

namespace Telenec.Mail.Statistics.Admin.Security;

public sealed class AdminApiKeyProvider : IAdminApiKeyProvider
{
    private const string ApiKeyConfigurationKey =
        "AdminApi:ApiKey";

    private readonly IAdminApiKeyStore _apiKeyStore;
    private readonly IConfiguration _configuration;

    public AdminApiKeyProvider(
        IAdminApiKeyStore apiKeyStore,
        IConfiguration configuration)
    {
        _apiKeyStore = apiKeyStore;
        _configuration = configuration;
    }

    public async Task<string> GetApiKeyAsync(
        CancellationToken cancellationToken = default)
    {
        var storedApiKey =
            await _apiKeyStore.ReadAsync(
                cancellationToken);

        if (!string.IsNullOrWhiteSpace(storedApiKey))
        {
            ValidateApiKey(
                storedApiKey,
                "Der im Windows Credential Manager gespeicherte Admin-API-Schlüssel");

            return storedApiKey;
        }

        var configuredApiKey =
            _configuration[ApiKeyConfigurationKey];

        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            throw new InvalidOperationException(
                "Es wurde kein Admin-API-Schlüssel gefunden. " +
                $"Weder der Windows Credential Manager noch '{ApiKeyConfigurationKey}' enthalten einen Schlüssel.");
        }

        ValidateApiKey(
            configuredApiKey,
            $"Der Admin-API-Schlüssel '{ApiKeyConfigurationKey}'");

        await _apiKeyStore.SaveAsync(
            configuredApiKey,
            cancellationToken);

        return configuredApiKey;
    }

    private static void ValidateApiKey(
        string apiKey,
        string description)
    {
        byte[] decodedApiKey;

        try
        {
            decodedApiKey =
                Convert.FromBase64String(apiKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"{description} ist kein gültiger Base64-Wert.",
                exception);
        }

        if (decodedApiKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"{description} muss mindestens 32 Bytes lang sein.");
        }
    }
}