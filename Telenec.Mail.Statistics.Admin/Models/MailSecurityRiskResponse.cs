namespace Telenec.Mail.Statistics.Admin.Models;

/// <summary>
/// Ausschliesslich lesende Ergebnisse des Beobachtungsmodus.
/// Keine aktiven Mailbox-Aktionen und kein Spamnachweis.
/// </summary>
public sealed record MailSecurityRiskResponse
{
    public bool Available { get; init; }
    public DateTimeOffset? GeneratedAtUtc { get; init; }
    public int EvaluatedAccounts { get; init; }
    public IReadOnlyList<MailSecurityRiskFindingResponse> Findings { get; init; } =
        Array.Empty<MailSecurityRiskFindingResponse>();
}

public sealed record MailSecurityRiskFindingResponse
{
    public string Account { get; init; } = string.Empty;
    public string Level { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public int? Passed { get; init; }
    public int Rejected { get; init; }
    public int RejectedPeriods { get; init; }
    public bool SendingLimitZero { get; init; }
    public double? RejectionSharePercent { get; init; }
}

public sealed record MailSecurityRiskRow(
    string Account,
    string Level,
    string Explanation,
    int? Passed,
    int Rejected,
    int RejectedPeriods,
    bool SendingLimitZero,
    double? RejectionSharePercent)
{
    public string SeverityText => Level switch
    {
        "Critical" => "Kritisch",
        "Warning" => "Warnung",
        "Observe" => "Beobachten",
        _ => "Unbekannt"
    };

    public string AllowedText => Passed?.ToString("N0") ?? "—";
    public string RejectedText => Rejected.ToString("N0");
    public string RejectionShareText =>
        RejectionSharePercent.HasValue
            ? RejectionSharePercent.Value.ToString("N1") + " %"
            : "—";

    public string LimitStatusText =>
        SendingLimitZero ? "Limit 0" : "Limit > 0";
}
