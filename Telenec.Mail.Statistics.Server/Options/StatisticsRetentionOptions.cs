namespace Telenec.Mail.Statistics.Server.Options;

public sealed class StatisticsRetentionOptions
{
    public const string SectionName =
        "Statistics";

    public int RetentionDays { get; set; } =
        365;
}