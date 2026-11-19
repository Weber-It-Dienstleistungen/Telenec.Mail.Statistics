namespace Telenec.Mail.Statistics.Admin.Models;

/// <summary>
/// Geschützte aggregierte Mailserverdaten. Keine Einzelereignisse.
/// Available=false ist keine Zählung von null E-Mails.
/// </summary>
public sealed record MailTrafficResponse
{
    public bool Available { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public DateTimeOffset? GeneratedAtUtc { get; init; }
    public DateTimeOffset WindowStartUtc { get; init; }
    public DateTimeOffset WindowEndUtc { get; init; }
    public IReadOnlyList<MailTrafficHourResponse> Hours { get; init; } =
        Array.Empty<MailTrafficHourResponse>();
    public IReadOnlyList<MailTrafficAccountResponse> TopSenders { get; init; } =
        Array.Empty<MailTrafficAccountResponse>();
    public IReadOnlyList<MailTrafficAccountResponse> TopRecipients { get; init; } =
        Array.Empty<MailTrafficAccountResponse>();
}

public sealed record MailTrafficHourResponse(
    DateTimeOffset HourStartUtc,
    int Outgoing,
    int Incoming);

public sealed record MailTrafficAccountResponse(
    string Account,
    int Count);
