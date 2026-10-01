namespace Telenec.Mail.Statistics.Admin.Configuration;

public interface IAdminApiBaseUrlProvider
{
    Task<Uri> GetBaseUriAsync(
        CancellationToken cancellationToken = default);

    Task ConfirmBaseUriAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default);
}