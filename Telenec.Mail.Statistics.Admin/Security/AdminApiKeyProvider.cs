
using Microsoft.Extensions.Configuration;
using Telenec.Mail.Statistics.Admin.Configuration;

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
        /*
         * Lokaler Entwicklungsmodus.
         *
         * Der temporäre Schlüssel wird ausschließlich
         * gemeinsam mit der festen lokalen
         * Entwicklungsadresse verwendet.
         *
         * Auf den Windows Credential Manager
         * wird in diesem Fall nicht zugegriffen.
         */
        if (LocalDevelopmentApiOverride.IsActive)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return LocalDevelopmentApiOverride
                .GetAdminApiKey();
        }

        /*
         * Bestehender Normalbetrieb.
         *
         * Gespeicherte Zugangsdaten werden
         * weiterhin bevorzugt verwendet.
         */
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

        return configuredApiKey;
    }

    public async Task ConfirmApiKeyAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        /*
         * Entwicklungsmodus:
         *
         * Prüfen, dass tatsächlich der lokale
         * Testschlüssel verwendet wurde.
         *
         * Der Testschlüssel darf keinesfalls
         * im Windows Credential Manager landen.
         */
        if (LocalDevelopmentApiOverride.IsActive)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var developmentApiKey =
                LocalDevelopmentApiOverride
                    .GetAdminApiKey();

            if (!string.Equals(
                    apiKey,
                    developmentApiKey,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Der verwendete Admin-API-Schlüssel stimmt nicht mit dem lokalen Entwicklungsmodus überein.");
            }

            return;
        }

        /*
         * Bestehender Normalbetrieb.
         */
        ValidateApiKey(
            apiKey,
            "Der erfolgreich verwendete Admin-API-Schlüssel");

        var storedApiKey =
            await _apiKeyStore.ReadAsync(
                cancellationToken);

        if (!string.IsNullOrWhiteSpace(storedApiKey))
        {
            return;
        }

        await _apiKeyStore.SaveAsync(
            apiKey,
            cancellationToken);
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
