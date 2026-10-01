namespace Telenec.Mail.Statistics.Admin.Security;

public interface IAdminApiKeyStore
{
    Task<string?> ReadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string apiKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        CancellationToken cancellationToken = default);
}