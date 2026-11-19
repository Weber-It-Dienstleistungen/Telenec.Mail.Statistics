using System.Text.Json;

namespace Telenec.Mail.Statistics.Server.MailSecurity;

/// <summary>
/// A read-only snapshot of Plesk Outgoing Mail Control data.
/// The account-level numbers are from the Plesk reporting window,
/// NOT a complete daily volume and NOT a spam classification.
/// </summary>
public sealed record MailSecurityPeriod(
    string Time,
    int? Passed,
    int? Rejected);

public sealed record MailSecurityAccount(
    string Account,
    int OutLimit,
    int? Passed,
    int? Rejected,
    IReadOnlyList<MailSecurityPeriod> Periods);

public sealed record MailSecurityOverview(
    bool Available,
    string StatusMessage,
    DateTimeOffset? GeneratedAtUtc,
    int MailboxCount,
    IReadOnlyList<MailSecurityAccount> Accounts);

/// <summary>
/// Reads an explicitly configured, protected snapshot.
/// Does not execute Plesk commands or modify mailboxes.
/// </summary>
public sealed class MailSecuritySnapshotReader
{
    private const long MaximumSnapshotBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(15);
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _clock;

    public MailSecuritySnapshotReader(
        IConfiguration configuration,
        TimeProvider clock)
    {
        _configuration = configuration;
        _clock = clock;
    }

    public async Task<MailSecurityOverview> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        MailSecurityOverview Unavailable(string message) =>
            new(false, message, null, 0, Array.Empty<MailSecurityAccount>());

        var configuredPath = _configuration["MailSecurity:SnapshotPath"];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Unavailable(
                "Mail-Sicherheit ist noch nicht mit der Plesk-Datenquelle verbunden.");
        }

        try
        {
            var path = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);

            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return Unavailable(
                    "Plesk-Datenquelle eingerichtet, aber noch kein Snapshot vorhanden.");
            }

            if (file.Length is <= 0 or > MaximumSnapshotBytes)
            {
                return Unavailable("Ungueltiger Mail-Security-Snapshot.");
            }

            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096, useAsync: true);

            var snapshot = await JsonSerializer.DeserializeAsync<SnapshotFile>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken);

            if (snapshot is null || !IsValid(snapshot))
            {
                return Unavailable(
                    "Die Plesk-Daten sind unvollstaendig oder haben ein unbekanntes Format.");
            }

            var age = _clock.GetUtcNow() - snapshot.GeneratedAtUtc;
            if (age < TimeSpan.FromMinutes(-2) || age > MaximumAge)
            {
                return Unavailable(
                    "Plesk-Sicherheitsdaten sind veraltet und werden nicht als aktuell angezeigt.");
            }

            return new MailSecurityOverview(
                true,
                "Plesk Outgoing Mail Control: aktuelle Daten, nur lesend. " +
                "Zahlen beziehen sich auf die gelieferten Berichtszeilen, " +
                "nicht auf einen vollstaendigen 24-Stunden-Zeitraum.",
                snapshot.GeneratedAtUtc,
                snapshot.MailboxCount,
                snapshot.Accounts!.Select(account => new MailSecurityAccount(
                    account.Account,
                    account.OutLimit,
                    account.Passed,
                    account.Rejected,
                    account.Periods!.Select(period => new MailSecurityPeriod(
                        period.Time,
                        period.Passed,
                        period.Rejected)).ToArray())).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            return Unavailable(
                "Plesk-Sicherheitsdaten konnten nicht sicher eingelesen werden.");
        }
    }

    private static bool IsValid(SnapshotFile snapshot)
    {
        if (snapshot.GeneratedAtUtc == default
            || !string.Equals(
                snapshot.Source, "plesk-mailmng-outgoing-hour",
                StringComparison.Ordinal)
            || snapshot.MailboxCount is < 1 or > 150_000
            || snapshot.Accounts is null
            || snapshot.Accounts.Count > 5_000
            || snapshot.Accounts.Count > snapshot.MailboxCount)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in snapshot.Accounts)
        {
            if (account is null
                || string.IsNullOrWhiteSpace(account.Account)
                || account.Account.Length > 254
                || account.Account.Count(c => c == '@') != 1
                || account.Account.Any(char.IsWhiteSpace)
                || account.Account.Any(char.IsControl)
                || !names.Add(account.Account)
                || account.OutLimit < 0
                || account.Passed is < 0
                || account.Rejected is < 0
                || account.Periods is not { Count: > 0 and <= 100 })
            {
                return false;
            }

            var timestamps = new HashSet<string>(StringComparer.Ordinal);
            long passed = 0;
            long rejected = 0;
            var hasPassed = false;
            var hasRejected = false;
            foreach (var period in account.Periods)
            {
                if (period is null
                    || string.IsNullOrWhiteSpace(period.Time)
                    || period.Time.Length > 64
                    || period.Time.Any(char.IsControl)
                    || !timestamps.Add(period.Time)
                    || period.Passed is < 0
                    || period.Rejected is < 0
                    || (period.Passed is null && period.Rejected is null))
                {
                    return false;
                }
                if (period.Passed.HasValue)
                {
                    hasPassed = true;
                    passed += period.Passed.Value;
                }
                if (period.Rejected.HasValue)
                {
                    hasRejected = true;
                    rejected += period.Rejected.Value;
                }
            }

            if ((hasPassed ? (long?)passed : null) != account.Passed
                || (hasRejected ? (long?)rejected : null) != account.Rejected
                || passed > int.MaxValue
                || rejected > int.MaxValue)
            {
                return false;
            }
        }

        return true;
    }

    public sealed class SnapshotFile
    {
        public DateTimeOffset GeneratedAtUtc { get; init; }
        public string Source { get; init; } = string.Empty;
        public int MailboxCount { get; init; }
        public List<SnapshotAccount>? Accounts { get; init; }
    }

    public sealed class SnapshotAccount
    {
        public string Account { get; init; } = string.Empty;
        public int OutLimit { get; init; }
        public int? Passed { get; init; }
        public int? Rejected { get; init; }
        public List<SnapshotPeriod>? Periods { get; init; }
    }

    public sealed class SnapshotPeriod
    {
        public string Time { get; init; } = string.Empty;
        public int? Passed { get; init; }
        public int? Rejected { get; init; }
    }
}
