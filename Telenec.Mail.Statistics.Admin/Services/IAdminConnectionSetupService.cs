namespace Telenec.Mail.Statistics.Admin.Services;

public interface IAdminConnectionSetupService
{
    Task ConfigureAsync(
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken = default);
}