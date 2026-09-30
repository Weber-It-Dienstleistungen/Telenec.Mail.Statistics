namespace Telenec.Mail.Statistics.Server.Contracts;

public sealed record HeartbeatRequest(
    string? InstallationId,
    string? ApplicationVersion,
    int ConsentVersion);