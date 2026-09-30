namespace Telenec.Mail.Statistics.Server.Models;

public sealed class Installation
{
    public string InstallationKey { get; set; } = string.Empty;

    public DateTime FirstSeenUtc { get; set; }

    public DateTime LastSeenUtc { get; set; }

    public string CurrentVersion { get; set; } = string.Empty;

    public int ConsentVersion { get; set; }
}