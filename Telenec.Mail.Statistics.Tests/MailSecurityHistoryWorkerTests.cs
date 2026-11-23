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

    public static async Task StartStopWithoutSourceAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var worker = fixture.CreateWorker(TimeSpan.FromMilliseconds(25));

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(120);

        using var stopTimeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));
        await worker.StopAsync(stopTimeout.Token);

        Assert(worker.ExecuteTask is { IsCompletedSuccessfully: true },
            "StopAsync muss einen auf den Timer wartenden Worker beenden.");

        await using var db = fixture.Context();
        Assert(await db.MailSecurityCheckpointStates.CountAsync() == 0
               && await db.MailSecurityIncidents.CountAsync() == 0,
            "Ohne gueltige Datenquelle darf der Host-Lebenszyklus nichts speichern.");
    }

    public static async Task PeriodicProcessingStopsCleanlyAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-3);
        await fixture.WriteSnapshotAsync(timestamp, 10, 30);

        var observed = new ObservedScopeFactory(fixture.ScopeFactory);
        using var worker = fixture.CreateWorker(
            TimeSpan.FromMilliseconds(40), observed);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(
                () => observed.CompletedCycles >= 1,
                "Der erste Hintergrundpoll wurde nicht beendet.");

            await fixture.WriteSnapshotAsync(
                timestamp.AddMinutes(1), 12, 70);
            await WaitUntilAsync(
                () => observed.CompletedCycles >= 4,
                "Der Timer hat keine weiteren Polls verarbeitet.");
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(5));
            await worker.StopAsync(stopTimeout.Token);
        }

        Assert(worker.ExecuteTask is { IsCompletedSuccessfully: true },
            "Nach StopAsync darf die Hintergrundaufgabe nicht weiterlaufen.");

        await using (var db = fixture.Context())
        {
            Assert((await db.MailSecurityIncidents.SingleAsync()).NewRejected == 40,
                "Wiederholte Timerpolls duerfen echte Deltas nur einmal zaehlen.");
            Assert((await db.MailSecurityCheckpointStates.SingleAsync())
                       .UpdatedAtUtc == timestamp.AddMinutes(1).UtcDateTime,
                "Der Hintergrundworker muss den letzten Snapshot speichern.");
        }

        var countAfterStop = observed.CompletedCycles;
        await fixture.WriteSnapshotAsync(
            timestamp.AddMinutes(2), 14, 100);
        await Task.Delay(160);
        Assert(observed.CompletedCycles == countAfterStop,
            "Nach StopAsync darf kein weiterer Poll laufen.");

        await using var verify = fixture.Context();
        Assert((await verify.MailSecurityIncidents.SingleAsync()).NewRejected == 40,
            "Ein neuer Snapshot nach Stop darf nicht verarbeitet werden.");
    }

    public static async Task SlowPollsNeverOverlapAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-3);
        await fixture.WriteSnapshotAsync(timestamp, 10, 30);

        // Die Scope-Freigabe ist absichtlich laenger als das Timerintervall.
        // Dadurch wuerde paralleles Scheduling als ActiveScopes > 1 auffallen.
        var observed = new ObservedScopeFactory(
            fixture.ScopeFactory, TimeSpan.FromMilliseconds(90));
        using var worker = fixture.CreateWorker(
            TimeSpan.FromMilliseconds(15), observed);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(
                () => observed.CompletedCycles >= 3,
                "Drei verlangsamte Hintergrundpolls wurden nicht beendet.");
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(5));
            await worker.StopAsync(stopTimeout.Token);
        }

        Assert(observed.PeakConcurrentScopes == 1,
            "Ein Worker darf nicht mehrere DB-Scopes parallel verarbeiten.");
        Assert(observed.CompletedCycles >= 3,
            "Die Hintergrundschleife muss wiederholt gearbeitet haben.");

        await using var db = fixture.Context();
        Assert(await db.MailSecurityIncidents.CountAsync() == 0,
            "Wiederholte gleiche Snapshots duerfen keine Vorfaelle erfinden.");
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition, string failureMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException(failureMessage);
            }

            await Task.Delay(20);
        }
    }

    private sealed class ObservedScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceScopeFactory _inner;
        private readonly TimeSpan _disposeDelay;
        private int _active;
        private int _peak;
        private int _completed;

        public ObservedScopeFactory(
            IServiceScopeFactory inner, TimeSpan disposeDelay = default)
        {
            _inner = inner;
            _disposeDelay = disposeDelay;
        }

        public int CompletedCycles => Volatile.Read(ref _completed);
        public int PeakConcurrentScopes => Volatile.Read(ref _peak);

        public IServiceScope CreateScope()
        {
            var scope = _inner.CreateScope();
            var active = Interlocked.Increment(ref _active);
            while (true)
            {
                var peak = Volatile.Read(ref _peak);
                if (active <= peak
                    || Interlocked.CompareExchange(
                        ref _peak, active, peak) == peak)
                {
                    break;
                }
            }

            return new ObservedScope(scope, this);
        }

        private void Completed()
        {
            Interlocked.Decrement(ref _active);
            Interlocked.Increment(ref _completed);
        }

        private sealed class ObservedScope : IServiceScope, IAsyncDisposable
        {
            private readonly IServiceScope _inner;
            private readonly ObservedScopeFactory _owner;

            public ObservedScope(
                IServiceScope inner, ObservedScopeFactory owner)
            {
                _inner = inner;
                _owner = owner;
            }

            public IServiceProvider ServiceProvider => _inner.ServiceProvider;

            public void Dispose()
            {
                try
                {
                    _inner.Dispose();
                }
                finally
                {
                    _owner.Completed();
                }
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (_owner._disposeDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(_owner._disposeDelay);
                    }

                    if (_inner is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync();
                    }
                    else
                    {
                        _inner.Dispose();
                    }
                }
                finally
                {
                    _owner.Completed();
                }
            }
        }
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
        public IServiceScopeFactory ScopeFactory =>
            _services.GetRequiredService<IServiceScopeFactory>();

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

        public MailSecurityHistoryWorker CreateWorker(
            TimeSpan? pollInterval = null,
            IServiceScopeFactory? scopeFactory = null)
        {
            var factory = scopeFactory ?? ScopeFactory;
            var logger = NullLogger<MailSecurityHistoryWorker>.Instance;

            return pollInterval is { } interval
                ? new MailSecurityHistoryWorker(
                    factory, _reader, logger, interval)
                : new MailSecurityHistoryWorker(
                    factory, _reader, logger);
        }

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
