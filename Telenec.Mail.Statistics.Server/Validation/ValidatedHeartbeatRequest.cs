namespace Telenec.Mail.Statistics.Server.Validation;

public sealed record ValidatedHeartbeatRequest(
    Guid InstallationId,
    string ApplicationVersion,
    int ConsentVersion);