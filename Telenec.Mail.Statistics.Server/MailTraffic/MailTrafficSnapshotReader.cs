using System.Text.Json;

namespace Telenec.Mail.Statistics.Server.MailTraffic;

/// <summary>
/// Ein stündlicher Aggregatwert. Zeiten sind UTC; jede Stunde steht
/// für genau ein disjunktes Zeitintervall.
/// </summary>
public sealed record MailTrafficHour(
    DateTimeOffset HourStartUtc,
    int Outgoing,
    int Incoming);

/// <summary>
/// Accountbezogene Aggregation; keine Betreffzeilen oder Nachrichteninhalte.
/// </summary>
public sealed record MailTrafficAccount(string Account, int Count);

/// <summary>
/// Geschützte Antwort an das Administrationspanel.
/// Available=false bedeutet ausdrücklich nicht "null E-Mails".
/// </summary>
public sealed record MailTrafficOverview(
    bool Available,
    string StatusMessage,
    DateTimeOffset? GeneratedAtUtc,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyList<MailTrafficHour> Hours,
    IReadOnlyList<MailTrafficAccount> TopSenders,
    IReadOnlyList<MailTrafficAccount> TopRecipients);

/// <summary>
/// Vertrag für eine später getrennt betriebene, serverseitige
/// Mail-Log-Aggregation. Das Monitoring liest ausschließlich
/// einen bereits aggregierten Snapshot, niemals Mailinhalte.
/// Ein Collector und der Zugriff auf MTA-Logs werden hier
/// bewusst weder vorausgesetzt noch automatisch aktiviert.
/// </summary>
public sealed class MailTrafficSnapshotReader
{
    private const long MaximumSnapshotBytes = 256 * 1024;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(15);

    private readonly IConfiguration _configuration;
    private readonly TimeProvider _clock;

    public MailTrafficSnapshotReader(
        IConfiguration configuration,
        TimeProvider clock)
    {
        _configuration = configuration;
        _clock = clock;
    }

    public async Task<MailTrafficOverview> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var start = now.AddHours(-24);

        MailTrafficOverview Unavailable(string message) =>
            new(
                Available: false,
                StatusMessage: message,
                GeneratedAtUtc: null,
                WindowStartUtc: start,
                WindowEndUtc: now,
                Hours: Array.Empty<MailTrafficHour>(),
                TopSenders: Array.Empty<MailTrafficAccount>(),
                TopRecipients: Array.Empty<MailTrafficAccount>());

        var configuredPath = _configuration["MailTraffic:SnapshotPath"];

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Unavailable(
                "Noch keine Mailverkehr-Datenquelle eingerichtet. " +
                "Erforderlich ist eine serverseitige Aggregation aus den MTA-Protokollen.");
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
                    "Die Mailverkehr-Datenquelle ist eingerichtet, liefert aber noch keinen Snapshot.");
            }

            if (file.Length == 0 || file.Length > MaximumSnapshotBytes)
            {
                return Unavailable("Der Mailverkehr-Snapshot hat ein ungültiges Dateiformat.");
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);

            var snapshot = await JsonSerializer.DeserializeAsync<MailTrafficSnapshotFile>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken);

            if (snapshot is null || !IsValid(snapshot))
            {
                return Unavailable(
                    "Die Mailverkehr-Datenquelle enthält keinen vollständigen 24-Stunden-Snapshot.");
            }

            var age = now - snapshot.GeneratedAtUtc;
            var windowAge = now - snapshot.WindowEndUtc;
            if (age < TimeSpan.FromMinutes(-2)
                || age > MaximumAge
                || windowAge < TimeSpan.FromMinutes(-2)
                || windowAge > MaximumAge)
            {
                return Unavailable(
                    "Die Mailverkehrsdaten sind veraltet. " +
                    "Die Anzeige wird nicht als aktuell ausgegeben.");
            }

            return new MailTrafficOverview(
                Available: true,
                StatusMessage: "Mailserverdaten verfügbar.",
                GeneratedAtUtc: snapshot.GeneratedAtUtc,
                WindowStartUtc: snapshot.WindowStartUtc,
                WindowEndUtc: snapshot.WindowEndUtc,
                Hours: snapshot.Hours!.AsReadOnly(),
                TopSenders: snapshot.TopSenders!.AsReadOnly(),
                TopRecipients: snapshot.TopRecipients!.AsReadOnly());
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
            // Keine Dateipfade oder Kontodaten in der API-Fehlermeldung.
            return Unavailable(
                "Der Mailverkehr-Snapshot konnte nicht sicher eingelesen werden.");
        }
    }

    private static bool IsValid(MailTrafficSnapshotFile snapshot)
    {
        if (snapshot.GeneratedAtUtc == default
            || snapshot.WindowStartUtc == default
            || snapshot.WindowEndUtc == default
            || snapshot.WindowEndUtc - snapshot.WindowStartUtc != TimeSpan.FromHours(24)
            || snapshot.Hours is not { Count: 24 }
            || snapshot.TopSenders is null
            || snapshot.TopRecipients is null
            || snapshot.TopSenders.Count > 3
            || snapshot.TopRecipients.Count > 3)
        {
            return false;
        }

        for (var i = 0; i < snapshot.Hours.Count; i++)
        {
            var hour = snapshot.Hours[i];
            if (hour is null
                || hour.HourStartUtc != snapshot.WindowStartUtc.AddHours(i)
                || hour.Outgoing < 0
                || hour.Incoming < 0)
            {
                return false;
            }
        }

        return ValidAccounts(snapshot.TopSenders)
            && ValidAccounts(snapshot.TopRecipients);
    }

    private static bool ValidAccounts(IReadOnlyList<MailTrafficAccount> accounts)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in accounts)
        {
            if (entry is null
                || entry.Count < 0
                || string.IsNullOrWhiteSpace(entry.Account)
                || entry.Account.Length > 254
                || !entry.Account.Contains('@')
                || entry.Account.Contains('\r')
                || entry.Account.Contains('\n')
                || !known.Add(entry.Account))
            {
                return false;
            }
        }

        return true;
    }

    public sealed class MailTrafficSnapshotFile
    {
        public DateTimeOffset GeneratedAtUtc { get; init; }
        public DateTimeOffset WindowStartUtc { get; init; }
        public DateTimeOffset WindowEndUtc { get; init; }
        public List<MailTrafficHour>? Hours { get; init; }
        public List<MailTrafficAccount>? TopSenders { get; init; }
        public List<MailTrafficAccount>? TopRecipients { get; init; }
    }
}
