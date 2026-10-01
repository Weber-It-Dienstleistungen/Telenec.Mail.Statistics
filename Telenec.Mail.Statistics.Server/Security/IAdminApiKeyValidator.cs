namespace Telenec.Mail.Statistics.Server.Security;

public interface IAdminApiKeyValidator
{
    bool IsValid(string? providedApiKey);
}