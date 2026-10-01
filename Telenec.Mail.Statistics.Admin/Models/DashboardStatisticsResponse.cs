namespace Telenec.Mail.Statistics.Admin.Models;

public sealed record DashboardStatisticsResponse(
    int TotalInstallations,
    int ActiveToday,
    int ActiveLast7Days,
    int ActiveLast30Days,
    int InactiveOver30Days);