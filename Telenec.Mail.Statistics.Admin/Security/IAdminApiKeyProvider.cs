namespace Telenec.Mail.Statistics.Admin.Security;

public interface IAdminApiKeyProvider
{
    Task<string> GetApiKeyAsync(
        CancellationToken cancellationToken = default);

    Task ConfirmApiKeyAsync(
        string apiKey,
        CancellationToken cancellationToken = default);
}