using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Telenec.Mail.Statistics.Server.Data;
using Telenec.Mail.Statistics.Server.MailSecurity;

namespace Telenec.Mail.Statistics.Tests;

/// <summary>
/// Sprint 43.4c: Testet den realen Worker-Durchlauf, jedoch ohne
/// Hintergrundtimer oder produktive Serverregistrierung.
/// Snapshot-Dateien und Accountdaten sind ausschliesslich synthetisch.
/// </summary>
internal static class MailSecurityHistoryWorkerTests
{
    public static async Task FirstCycleCreatesOnlyBaselineAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-3);
        await fixture.WriteSnapshotAsync(timestamp, passed: 10, rejected: 300);

        await fixture.CreateWorker().ProcessSnapshotOnceAsync(
            CancellationToken.None);

        await using var db = fixture.Context();
        Assert(await db.MailSecurityCheckpointStates.CountAsync() == 1,
            "Ein gueltiger erster Poll muss den Checkpoint speichern.");
        Assert(await db.MailSecurityIncidents.CountAsync() == 0,
            "Hohe historische Ablehnungen duerfen keinen Vorfall erfinden.");
        Assert((await db.MailSecurityCheckpointStates.SingleAsync())
                   .UpdatedAtUtc == timestamp.UtcDateTime,
            "Der gespeicherte Zeitpunkt muss dem Snapshot entsprechen.");
    }

    public static async Task RepeatedCycleIsIdempotentAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-3);
        var worker = fixture.CreateWorker();

        await fixture.WriteSnapshotAsync(timestamp, passed: 10, rejected: 30);
        await worker.ProcessSnapshotOnceAsync(CancellationToken.None);

        await fixture.WriteSnapshotAsync(
            timestamp.AddMinutes(1), passed: 12, rejected: 70);
        await worker.ProcessSnapshotOnceAsync(CancellationToken.None);
        await worker.ProcessSnapshotOnceAsync(CancellationToken.None);

        await using var db = fixture.Context();
        var incidents = await db.MailSecurityIncidents.ToListAsync();
        Assert(incidents.Count == 1
               && incidents[0].NewRejected == 40
               && incidents[0].NewPassed == 2,
            "Wiederholung darf die nachgewiesenen Differenzen nicht verdoppeln.");
        Assert((await db.MailSecurityCheckpointStates.SingleAsync())
                   .UpdatedAtUtc == timestamp.AddMinutes(1).UtcDateTime,
            "Wiederholung darf den Checkpoint nicht veraendern.");
    }

    public static async Task UnavailableSourcesPreserveCheckpointAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-3);
        var worker = fixture.CreateWorker();

        await fixture.WriteSnapshotAsync(timestamp, passed: 5, rejected: 10);
        await worker.ProcessSnapshotOnceAsync(CancellationToken.None);

        // Kein Snapshot: keine Datenbankveraenderung.
        File.Delete(fixture.SnapshotPath);
        await worker.ProcessSnapshotOnceAsync(CancellationToken.None);

        // Kaputte JSON-Datei: Reader meldet unavailable.
        await File.WriteAllTextAsync(fixture.SnapshotPath, "{invalid json");
        await worker.ProcessSnapshotOnceAsync(CancellationToken.None);

        // Veraltete Quelldaten: ebenfalls keine Entwarnung oder Buchung.
        await fixture.WriteSnapshotAsync(
            timestamp.AddHours(-1), passed: 12, rejected: 900);
        await worker.ProcessSnapshotOnceAsync(CancellationToken.None);

        await using (var db = fixture.Context())
        {
            Assert((await db.MailSecurityCheckpointStates.SingleAsync())
                       .UpdatedAtUtc == timestamp.UtcDateTime,
                "Unguelle Quellen duerfen Checkpoint nicht fortschreiben.");
            Assert(await db.MailSecurityIncidents.CountAsync() == 0,
                "Fehlende oder alte Quellen duerfen keine Faelle erfinden.");
        }

        // Anschliessend ist ein neuer, gueltiger Poll wieder moeglich.
        await fixture.WriteSnapshotAsync(
            timestamp.AddMinutes(1), passed: 6, rejected: 14);
        await worker.ProcessSnapshotOnceAsync(CancellationToken.None);

        await using var reloaded = fixture.Context();
        Assert((await reloaded.MailSecurityIncidents.SingleAsync()).NewRejected == 4,
            "Nach Datenquellenfehler muss normale Verarbeitung moeglich sein.");
    }

    public static async Task SqliteFailureRollsBackAndCanRetryAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-3);
        var worker = fixture.CreateWorker();

        await fixture.WriteSnapshotAsync(timestamp, passed: 10, rejected: 20);
        await worker.ProcessSnapshotOnceAsync(CancellationToken.None);

        await using (var db = fixture.Context())
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER reject_worker_insert " +
                "BEFORE INSERT ON MailSecurityIncidents " +
                "BEGIN SELECT RAISE(ABORT, 'synthetic worker failure'); END;");
        }

        var next = timestamp.AddMinutes(1);
        await fixture.WriteSnapshotAsync(next, passed: 12, rejected: 60);
        // Worker protokolliert den Fehler, darf aber selbst nicht abstuerzen.
        await worker.ProcessSnapshotOnceAsync(CancellationToken.None);

        await using (var db = fixture.Context())
        {
            Assert((await db.MailSecurityCheckpointStates.SingleAsync())
                       .UpdatedAtUtc == timestamp.UtcDateTime,
                "Fehlgeschlagener Poll darf den Checkpoint nicht verschieben.");
            Assert(await db.MailSecurityIncidents.CountAsync() == 0,
                "Eine teilweise Buchung darf nicht stehen bleiben.");
            await db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER reject_worker_insert;");
        }

        await worker.ProcessSnapshotOnceAsync(CancellationToken.None);

        await using var recovered = fixture.Context();
        Assert((await recovered.MailSecurityIncidents.SingleAsync()).NewRejected == 40
               && (await recovered.MailSecurityCheckpointStates.SingleAsync())
                      .UpdatedAtUtc == next.UtcDateTime,
            "Der identische Snapshot muss nach Fehlerbehebung exakt einmal buchbar sein.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;
        private readonly DbContextOptions<StatisticsDbContext> _options;
        private readonly MailSecuritySnapshotReader _reader;

        public string SnapshotPath { get; }

        private Fixture(SqliteConnection connection, ServiceProvider services,
            DbContextOptions<StatisticsDbContext> options, string path)
        {
            _connection = connection;
            _services = services;
            _options = options;
            SnapshotPath = path;

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MailSecurity:SnapshotPath"] = path
                })
                .Build();
            _reader = new MailSecuritySnapshotReader(
                configuration, TimeProvider.System);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddDbContext<StatisticsDbContext>(
                options => options.UseSqlite(connection));
            var provider = services.BuildServiceProvider();

            var options = new DbContextOptionsBuilder<StatisticsDbContext>()
                .UseSqlite(connection).Options;
            var path = Path.Combine(Path.GetTempPath(),
                "telenec-mail-worker-" + Guid.NewGuid().ToString("N") + ".json");
            var fixture = new Fixture(connection, provider, options, path);
            await using var db = fixture.Context();
            await db.Database.MigrateAsync();
            return fixture;
        }

        public StatisticsDbContext Context() => new(_options);

        public MailSecurityHistoryWorker CreateWorker() =>
            new(
                _services.GetRequiredService<IServiceScopeFactory>(),
                _reader,
                NullLogger<MailSecurityHistoryWorker>.Instance);

        public async Task WriteSnapshotAsync(
            DateTimeOffset timestamp, int passed, int rejected)
        {
            var snapshot = new MailSecuritySnapshotReader.SnapshotFile
            {
                GeneratedAtUtc = timestamp,
                Source = "plesk-mailmng-outgoing-hour",
                MailboxCount = 1,
                Accounts = new()
                {
                    new MailSecuritySnapshotReader.SnapshotAccount
                    {
                        Account = "synthetic@example.test",
                        OutLimit = 150,
                        Passed = passed,
                        Rejected = rejected,
                        Periods = new()
                        {
                            new MailSecuritySnapshotReader.SnapshotPeriod
                            {
                                Time = "2026-11-23 08",
                                Passed = passed,
                                Rejected = rejected
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(
                snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await File.WriteAllTextAsync(SnapshotPath, json);
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await _connection.DisposeAsync();
            if (File.Exists(SnapshotPath))
            {
                File.Delete(SnapshotPath);
            }
        }
    }
}
