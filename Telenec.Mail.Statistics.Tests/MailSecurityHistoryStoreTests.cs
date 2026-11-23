using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Telenec.Mail.Statistics.Server.Data;
using Telenec.Mail.Statistics.Server.MailSecurity;

namespace Telenec.Mail.Statistics.Tests;

/// <summary>
/// Sprint 43.4b: echte SQLite-In-Memory-Datenbank, ausschliesslich
/// synthetische Konten. Keine produktiven Daten oder Mailserveraktionen.
/// </summary>
internal static class MailSecurityHistoryStoreTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 11, 23, 8, 0, 0, TimeSpan.Zero);

    public static async Task MigrationCreatesIsolatedTablesAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Context();

        var migrations = await db.Database.GetAppliedMigrationsAsync();
        Assert(migrations.Contains("20260930113558_InitialCreate")
               && migrations.Contains("20261123070000_AddMailSecurityHistory"),
            "Bestehende und neue SQLite-Migration muessen angewendet sein.");
        Assert(await db.Installations.CountAsync() == 0,
            "Die Installationsstatistik bleibt eigenstaendig.");
        Assert(await db.MailSecurityIncidents.CountAsync() == 0,
            "Keine historischen Vorfaelle erfinden.");
        Assert(await db.MailSecurityCheckpointStates.CountAsync() == 0,
            "Vor dem ersten Poll existiert noch kein Checkpoint.");
    }

    public static async Task FirstSnapshotIsBaselineOnlyAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Context())
        {
            var result = await new MailSecurityHistoryStore(db)
                .ProcessAsync(Snapshot(T0, 0, 810));
            Assert(result.Status == MailSecuritySnapshotChangeStatus.InitialBaseline,
                "Hohe historische Werte sind zuerst nur eine Basislinie.");
            Assert(result.CreatedIncidents == 0 && result.NewRejected == 0,
                "Erster Snapshot darf keine neuen Ablehnungen buchen.");
        }

        await using var reloaded = fixture.Context();
        Assert(await reloaded.MailSecurityCheckpointStates.CountAsync() == 1
               && await reloaded.MailSecurityIncidents.CountAsync() == 0,
            "Checkpoint muss ohne Vorfall dauerhaft gespeichert sein.");
    }

    public static async Task IncreasingCountersCreateOneIncidentAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Process(fixture, Snapshot(T0, 10, 30));
        var result = await Process(fixture, Snapshot(T0.AddMinutes(5), 11, 65));
        Assert(result.CreatedIncidents == 1 && result.NewRejected == 35,
            "Nur 35 neue Ablehnungen duerfen einen Vorfall erzeugen.");

        await using var db = fixture.Context();
        var incidents = await db.MailSecurityIncidents.ToListAsync();
        Assert(incidents.Count == 1,
            "Es muss genau einen aggregierten Account-Vorfall geben.");
        Assert(incidents[0].NewRejected == 35
               && incidents[0].NewPassed == 1
               && incidents[0].ClosedAtUtc is null,
            "Vorfall muss echte Differenzen statt historische Gesamtzahlen enthalten.");
    }

    public static async Task DuplicateAndOlderPollAreIdempotentAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Process(fixture, Snapshot(T0, 10, 30));
        await Process(fixture, Snapshot(T0.AddMinutes(5), 11, 65));

        var repeated = await Process(fixture,
            Snapshot(T0.AddMinutes(5), 11, 999));
        var older = await Process(fixture,
            Snapshot(T0.AddMinutes(2), 11, 999));
        var newerEqual = await Process(fixture,
            Snapshot(T0.AddMinutes(10), 11, 65));

        Assert(repeated.Status == MailSecuritySnapshotChangeStatus.OutOfOrder
               && older.Status == MailSecuritySnapshotChangeStatus.OutOfOrder
               && newerEqual.Status == MailSecuritySnapshotChangeStatus.NoCountGrowth,
            "Doppelte/alte und unveraenderte Polls muessen getrennt bleiben.");

        await using var db = fixture.Context();
        Assert(await db.MailSecurityIncidents.CountAsync() == 1,
            "Keine doppelten Vorfaelle.");
        Assert((await db.MailSecurityIncidents.SingleAsync()).NewRejected == 35,
            "Keine doppelt gezaehlten Ablehnungen.");
    }

    public static async Task NewDbContextKeepsCheckpointAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Process(fixture, Snapshot(T0, 10, 30));
        await Process(fixture, Snapshot(T0.AddMinutes(5), 11, 65));

        // Jede Operation erhaelt einen neuen DbContext wie bei
        // einem Neustart des künftigen periodischen Workers.
        var result = await Process(fixture,
            Snapshot(T0.AddMinutes(10), 13, 74));
        Assert(result.UpdatedIncidents == 1
               && result.CreatedIncidents == 0
               && result.NewRejected == 9,
            "Checkpoint muss einen Prozess-/Contextneustart ueberleben.");

        await using var db = fixture.Context();
        var incident = await db.MailSecurityIncidents.SingleAsync();
        Assert(incident.NewRejected == 44 && incident.NewPassed == 3,
            "Fortschritt muss auf denselben Vorfall gebucht werden.");
    }

    public static async Task UnavailableSnapshotChangesNothingAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Process(fixture, Snapshot(T0, 10, 30));
        await Process(fixture, Snapshot(T0.AddMinutes(5), 11, 65));

        var result = await Process(fixture,
            new MailSecurityOverview(
                false, "Keine aktuellen Daten", null, 0,
                Array.Empty<MailSecurityAccount>()));
        Assert(result.Status == MailSecuritySnapshotChangeStatus.Unavailable,
            "Ein fehlender Snapshot ist keine Entwarnung.");

        await using var db = fixture.Context();
        Assert((await db.MailSecurityCheckpointStates.SingleAsync())
                   .UpdatedAtUtc == T0.AddMinutes(5).UtcDateTime,
            "Nicht verfuegbare Daten duerfen den letzten Checkpoint nicht loeschen.");
        Assert((await db.MailSecurityIncidents.SingleAsync()).NewRejected == 35,
            "Bereits beobachtete Ereignisse bleiben bestehen.");
    }

    public static async Task IdleIncidentClosesAndLaterReopensAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Process(fixture, Snapshot(T0, 10, 30));
        await Process(fixture, Snapshot(T0.AddMinutes(5), 11, 65));

        // Neue Stunde mehr als 24h spaeter: neue Berichtszeile
        // ist erst Basislinie, der alte Vorfall wird abgeschlossen.
        var idle = await Process(fixture, Snapshot(
            T0.AddHours(25), 0, 1, period: "2026-11-24 09"));
        Assert(idle.ClosedIncidents == 1,
            "Inaktive Vorfaelle werden nach 24h ohne neue Ablehnungen geschlossen.");

        var newActivity = await Process(fixture, Snapshot(
            T0.AddHours(25).AddMinutes(5), 1, 33,
            period: "2026-11-24 09"));
        Assert(newActivity.CreatedIncidents == 1 && newActivity.NewRejected == 32,
            "Nach Abschluss duerfen neue Ablehnungen einen neuen Vorfall bilden.");

        await using var db = fixture.Context();
        var items = await db.MailSecurityIncidents
            .OrderBy(x => x.FirstObservedUtc).ToListAsync();
        Assert(items.Count == 2 && items[0].ClosedAtUtc is not null
               && items[1].ClosedAtUtc is null && items[0].Id != items[1].Id,
            "Die Historie muss zwei getrennte Vorfaelle aufbewahren.");
    }

    public static async Task ClosedIncidentsArePrunedAfterThirtyDaysAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Process(fixture, Snapshot(T0, 10, 30));
        await Process(fixture, Snapshot(T0.AddMinutes(5), 11, 65));
        await Process(fixture, Snapshot(
            T0.AddHours(25), 0, 1, period: "2026-11-24 09"));

        var afterRetention = await Process(fixture, Snapshot(
            T0.AddDays(32), 0, 1, period: "2026-12-25 08"));
        Assert(afterRetention.CreatedIncidents == 0,
            "Neuer Berichtszeitraum nach Datenluecke ist wieder eine Basislinie.");

        await using var db = fixture.Context();
        Assert(await db.MailSecurityIncidents.CountAsync() == 0,
            "Abgeschlossene Vorfaelle muessen nach 30 Tagen geloescht werden.");
    }

    public static async Task StorageErrorRollsBackEntireCheckpointAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Process(fixture, Snapshot(T0, 10, 30));

        await using (var db = fixture.Context())
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER fail_incident BEFORE INSERT ON MailSecurityIncidents " +
                "BEGIN SELECT RAISE(ABORT, 'synthetic test failure'); END;");
        }

        var failed = false;
        try
        {
            await Process(fixture, Snapshot(T0.AddMinutes(5), 11, 65));
        }
        catch (DbUpdateException)
        {
            failed = true;
        }

        Assert(failed, "Ein absichtlich ausgeloester DB-Fehler muss sichtbar sein.");

        await using var check = fixture.Context();
        var state = await check.MailSecurityCheckpointStates.SingleAsync();
        Assert(state.UpdatedAtUtc == T0.UtcDateTime,
            "Bei gescheiterter Vorfallspeicherung darf Checkpoint nicht fortschreiten.");
        Assert(await check.MailSecurityIncidents.CountAsync() == 0,
            "Transaktion muss auch angefangene Vorfall-Aenderungen zurueckrollen.");
    }

    public static async Task RiskLevelAndLimitAreOnlyMetadataAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Process(fixture, Snapshot(T0, 2, 300, limit: 150));
        await Process(fixture, Snapshot(
            T0.AddMinutes(5), 3, 500, limit: 0));

        await using var db = fixture.Context();
        var incident = await db.MailSecurityIncidents.SingleAsync();
        Assert(incident.SendingLimitZero,
            "Aktuelles Versandlimit 0 muss sichtbar bleiben.");
        Assert(incident.MaxRiskLevel >= MailSecurityRiskLevel.Warning,
            "Risiko-Prioritaet wird aus dem gueltigen Snapshot uebernommen.");
        Assert(incident.NewRejected == 200,
            "Historische 300 Ablehnungen werden nicht doppelt gezaehlt.");
    }

    public static async Task HistoricalRiskDoesNotEscalateTinyDeltaAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        // 810 alte Ablehnungen koennen in der Bestands-Risk-Engine
        // Warnungen ausloesen, sind aber kein neuer Vorfall.
        await Process(fixture, Snapshot(T0, 1, 810));
        await Process(fixture, Snapshot(T0.AddMinutes(5), 1, 811));

        await using var db = fixture.Context();
        var incident = await db.MailSecurityIncidents.SingleAsync();
        Assert(incident.NewRejected == 1
               && incident.MaxRiskLevel == MailSecurityRiskLevel.None,
            "Eine einzige neue Ablehnung darf historische 810 nicht uebernehmen.");
    }

    public static async Task NewRejectionThresholdsAreProgressiveAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Process(fixture, Snapshot(T0, 0, 0));
        await Process(fixture, Snapshot(T0.AddMinutes(5), 0, 25));

        await using (var db = fixture.Context())
        {
            Assert((await db.MailSecurityIncidents.SingleAsync()).MaxRiskLevel
                   == MailSecurityRiskLevel.Observe,
                "25 neue Ablehnungen sind vorerst Beobachtung.");
        }

        await Process(fixture, Snapshot(T0.AddMinutes(10), 0, 100));
        await using (var db = fixture.Context())
        {
            Assert((await db.MailSecurityIncidents.SingleAsync()).MaxRiskLevel
                   == MailSecurityRiskLevel.Warning,
                "100 neue Ablehnungen erhoehen den Pruefbedarf.");
        }

        await Process(fixture, Snapshot(T0.AddMinutes(15), 0, 500));
        await using var check = fixture.Context();
        var incident = await check.MailSecurityIncidents.SingleAsync();
        Assert(incident.NewRejected == 500
               && incident.MaxRiskLevel == MailSecurityRiskLevel.Critical,
            "500 tatsaechlich neue Ablehnungen werden kritisch markiert.");
    }

    public static async Task PassedOnlyTrafficDoesNotCreateIncidentAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Process(fixture, Snapshot(T0, 10, 0));
        var result = await Process(fixture, Snapshot(
            T0.AddMinutes(5), 20, 0));

        Assert(result.Status == MailSecuritySnapshotChangeStatus.ChangesObserved
               && result.CreatedIncidents == 0,
            "Erfolgreicher Mailverkehr allein ist kein Sicherheitsvorfall.");

        await using var db = fixture.Context();
        Assert(await db.MailSecurityIncidents.CountAsync() == 0
               && (await db.MailSecurityCheckpointStates.SingleAsync())
                   .UpdatedAtUtc == T0.AddMinutes(5).UtcDateTime,
            "Auch ohne neuen Vorfall muss der Checkpoint fortschreiten.");
    }

    public static async Task HistoryCanBeReadWithoutMutatingItAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Process(fixture, Snapshot(T0, 10, 30));
        await Process(fixture, Snapshot(T0.AddMinutes(5), 11, 65));

        await using var db = fixture.Context();
        var store = new MailSecurityHistoryStore(db);
        var history = await store.ReadAsync();

        Assert(history.LastProcessedUtc == T0.AddMinutes(5).UtcDateTime
               && history.Incidents.Count == 1
               && history.Incidents[0].NewRejected == 35
               && history.Incidents[0].ClosedAtUtc is null,
            "Gelesene Historie muss zu den gespeicherten Zaehlern passen.");
        Assert(await db.MailSecurityIncidents.CountAsync() == 1,
            "Reiner Lesezugriff darf keine Daten veraendern.");
    }

    private static async Task<MailSecurityHistoryWriteResult> Process(
        Fixture fixture, MailSecurityOverview snapshot)
    {
        await using var db = fixture.Context();
        return await new MailSecurityHistoryStore(db).ProcessAsync(snapshot);
    }

    private static MailSecurityOverview Snapshot(
        DateTimeOffset timestamp, int? passed, int? rejected,
        int limit = 150, string period = "2026-11-23 08")
    {
        var account = new MailSecurityAccount(
            "test@example.test", limit, passed, rejected,
            new[] { new MailSecurityPeriod(period, passed, rejected) });

        return new MailSecurityOverview(
            true, "Synthetischer Test", timestamp, 1,
            new[] { account });
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
        private readonly DbContextOptions<StatisticsDbContext> _options;

        private Fixture(SqliteConnection connection)
        {
            _connection = connection;
            _options = new DbContextOptionsBuilder<StatisticsDbContext>()
                .UseSqlite(connection).Options;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);

            await using var db = fixture.Context();
            await db.Database.MigrateAsync();
            return fixture;
        }

        public StatisticsDbContext Context() => new(_options);

        public async ValueTask DisposeAsync() =>
            await _connection.DisposeAsync();
    }
}
