using System.Security.Cryptography;
using System.Text;

namespace Telenec.Mail.Statistics.Server.Security;

public sealed class AdminApiKeyValidator : IAdminApiKeyValidator
{
    private const string ConfigurationKey = "Statistics:AdminApiKey";

    private readonly byte[] _expectedApiKey;

    public AdminApiKeyValidator(IConfiguration configuration)
    {
        var configuredApiKey = configuration[ConfigurationKey];

        if (string.IsNullOrWhiteSpace(configuredApiKey))
        {
            throw new InvalidOperationException(
                $"Der Admin-API-Schlüssel '{ConfigurationKey}' wurde nicht konfiguriert.");
        }

        byte[] decodedApiKey;

        try
        {
            decodedApiKey = Convert.FromBase64String(configuredApiKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"Der Admin-API-Schlüssel '{ConfigurationKey}' ist kein gültiger Base64-Wert.",
                exception);
        }

        if (decodedApiKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"Der Admin-API-Schlüssel '{ConfigurationKey}' muss mindestens 32 Bytes lang sein.");
        }

        _expectedApiKey = decodedApiKey;
    }

    public bool IsValid(string? providedApiKey)
    {
        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            return false;
        }

        byte[] providedBytes;

        try
        {
            providedBytes = Convert.FromBase64String(providedApiKey);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            _expectedApiKey,
            providedBytes);
    }
}