namespace Telenec.Mail.Statistics.Admin.Configuration;

public interface IAdminApiBaseUrlStore
{
    Task<string?> ReadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string baseUrl,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        CancellationToken cancellationToken = default);
}