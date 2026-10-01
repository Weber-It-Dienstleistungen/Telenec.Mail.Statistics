namespace Telenec.Mail.Statistics.Admin.Security;

public interface IAdminApiKeyProvider
{
    Task<string> GetApiKeyAsync(
        CancellationToken cancellationToken = default);
}