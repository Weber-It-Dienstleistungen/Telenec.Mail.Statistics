namespace Telenec.Mail.Statistics.Admin.Models;

/// <summary>
/// Read-only Plesk Outgoing Mail Control statistics.
/// Only delivered reporting periods are represented; no complete
/// 24-hour account history is implied.
/// </summary>
public sealed record MailSecurityResponse
{
    public bool Available { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public DateTimeOffset? GeneratedAtUtc { get; init; }
    public int MailboxCount { get; init; }
    public IReadOnlyList<MailSecurityAccountResponse> Accounts { get; init; } =
        Array.Empty<MailSecurityAccountResponse>();
}

public sealed record MailSecurityAccountResponse
{
    public string Account { get; init; } = string.Empty;
    public int OutLimit { get; init; }
    public int? Passed { get; init; }
    public int? Rejected { get; init; }
    public IReadOnlyList<MailSecurityPeriodResponse> Periods { get; init; } =
        Array.Empty<MailSecurityPeriodResponse>();
}

public sealed record MailSecurityPeriodResponse
{
    public string Time { get; init; } = string.Empty;
    public int? Passed { get; init; }
    public int? Rejected { get; init; }
}

public sealed record MailSecurityPeriodRow(
    string Time,
    string PassedText,
    string RejectedText);

public sealed record MailSecurityAccountRow(
    string Account,
    int OutLimit,
    int? Passed,
    int? Rejected,
    IReadOnlyList<MailSecurityPeriodRow> Periods)
{
    public string LimitText => OutLimit.ToString("N0") + " / Stunde";
    public string PassedText => Passed?.ToString("N0") ?? "—";
    public string RejectedText => Rejected?.ToString("N0") ?? "—";

    public string StatusText =>
        Rejected is > 0
            ? "Ablehnungen"
            : OutLimit == 0
                ? "Limit 0 – Einstellung prüfen"
                : OutLimit > 0 && Passed >= OutLimit
                    ? "Limit erreicht (Hinweis)"
                    : Rejected is null
                        ? "Keine Ablehnungsdaten"
                        : "Keine Ablehnung gemeldet";
}
