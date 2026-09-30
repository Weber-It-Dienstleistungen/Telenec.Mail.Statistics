using System.Security.Cryptography;
using System.Text;

namespace Telenec.Mail.Statistics.Server.Security;

public sealed class HmacInstallationKeyService : IInstallationKeyService
{
    private const string ConfigurationKey = "Statistics:HmacKey";
    private const int MinimumKeyLengthBytes = 32;

    private readonly byte[] _key;

    public HmacInstallationKeyService(IConfiguration configuration)
    {
        var configuredKey = configuration[ConfigurationKey];

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException(
                $"Der HMAC-Schlüssel '{ConfigurationKey}' wurde nicht konfiguriert.");
        }

        try
        {
            _key = Convert.FromBase64String(configuredKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"Der HMAC-Schlüssel '{ConfigurationKey}' muss als Base64-Wert vorliegen.",
                exception);
        }

        if (_key.Length < MinimumKeyLengthBytes)
        {
            throw new InvalidOperationException(
                $"Der HMAC-Schlüssel '{ConfigurationKey}' muss mindestens " +
                $"{MinimumKeyLengthBytes} Bytes lang sein.");
        }
    }

    public string CreateInstallationKey(Guid installationId)
    {
        var normalizedInstallationId = installationId
            .ToString("D")
            .ToLowerInvariant();

        var inputBytes = Encoding.UTF8.GetBytes(normalizedInstallationId);

        using var hmac = new HMACSHA256(_key);

        var hash = hmac.ComputeHash(inputBytes);

        return Convert.ToHexString(hash)
            .ToLowerInvariant();
    }
}