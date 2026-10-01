namespace Telenec.Mail.Statistics.Server.Services;

public sealed record DashboardStatistics(
    int TotalInstallations,
    int ActiveToday,
    int ActiveLast7Days,
    int ActiveLast30Days,
    int InactiveOver30Days);