namespace Telenec.Mail.Statistics.Server.Security;

public interface IInstallationKeyService
{
    string CreateInstallationKey(Guid installationId);
}