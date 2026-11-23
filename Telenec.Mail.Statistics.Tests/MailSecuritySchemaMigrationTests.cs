using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Telenec.Mail.Statistics.Server.Data;
using Telenec.Mail.Statistics.Server.MailSecurity;
using Telenec.Mail.Statistics.Server.Models;

namespace Telenec.Mail.Statistics.Tests;

/// <summary>
/// Sprint 43.4f: Migrationstests mit separater SQLite-In-Memory-DB.
/// Keine produktiven Installationsdaten und keine Mailserver-Aktionen.
/// </summary>
internal static class MailSecuritySchemaMigrationTests
{
    public static async Task FreshDatabaseWithoutPermissionOnlyCreatesInstallationsAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Context();
        await MailSecuritySchemaMigrationPolicy.MigrateAsync(db, Config());

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert(applied.SequenceEqual(new[]
        {
            MailSecuritySchemaMigrationPolicy.InitialMigration
        }), "Ohne Freigabe darf nur die vorhandene Basismigration laufen.");

        Assert(await HasTable(db, "Installations"),
            "Neue Serverinstallation muss weiterhin funktionsfaehig sein.");
        Assert(!await HasTable(db, "MailSecurityIncidents")
               && !await HasTable(db, "MailSecurityCheckpointStates"),
            "Account-Historientabellen duerfen nicht ungefragt entstehen.");
    }

    public static async Task ExistingInstallationsRemainWithFlagOffAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Context();
        await db.Database.MigrateAsync(
            MailSecuritySchemaMigrationPolicy.InitialMigration);
        db.Installations.Add(SyntheticInstallation());
        await db.SaveChangesAsync();

        await MailSecuritySchemaMigrationPolicy.MigrateAsync(db, Config());

        Assert(await db.Installations.CountAsync() == 1,
            "Bestehende Installationsdaten duerfen sich nicht aendern.");
        Assert(!await HasTable(db, "MailSecurityIncidents"),
            "Default-Konfiguration darf die History-Migration nicht ausfuehren.");
    }

    public static async Task ApprovedMigrationPreservesExistingInstallationsAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Context();
        await db.Database.MigrateAsync(
            MailSecuritySchemaMigrationPolicy.InitialMigration);
        db.Installations.Add(SyntheticInstallation());
        await db.SaveChangesAsync();

        await MailSecuritySchemaMigrationPolicy.MigrateAsync(
            db, Config(schema: "true"));

        Assert(await db.Installations.CountAsync() == 1,
            "Freigabe darf bestehende Installation nicht verlieren.");
        Assert(await HasTable(db, "MailSecurityIncidents")
               && await HasTable(db, "MailSecurityCheckpointStates"),
            "Nur mit Schemafreigabe duerfen History-Tabellen entstehen.");
        Assert((await db.Database.GetAppliedMigrationsAsync())
                   .Contains(MailSecuritySchemaMigrationPolicy.HistoryMigration),
            "Freigegebene Migration muss im EF-Protokoll stehen.");
    }

    public static async Task DisabledAfterApprovalNeverRollsBackAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Context();
        await MailSecuritySchemaMigrationPolicy.MigrateAsync(
            db, Config(schema: "true"));

        db.MailSecurityCheckpointStates.Add(new MailSecurityCheckpointState
        {
            Id = 1,
            CheckpointJson = "{\"synthetic\":true}",
            UpdatedAtUtc = new DateTime(2026, 11, 23, 8, 0, 0,
                DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        await MailSecuritySchemaMigrationPolicy.MigrateAsync(
            db, Config(schema: "false"));

        Assert(await db.MailSecurityCheckpointStates.CountAsync() == 1,
            "Schemaflag=false darf weder Tabellen noch Daten loeschen.");
        Assert((await db.Database.GetAppliedMigrationsAsync())
                   .Contains(MailSecuritySchemaMigrationPolicy.HistoryMigration),
            "Ein angewendetes Schema darf nicht rueckmigriert werden.");
    }

    public static async Task WorkerCannotStartWithoutUnapprovedSchemaAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Context();
        await db.Database.MigrateAsync(
            MailSecuritySchemaMigrationPolicy.InitialMigration);

        var threw = false;
        try
        {
            await MailSecuritySchemaMigrationPolicy.MigrateAsync(
                db, Config(worker: "true"));
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert(threw && !await HasTable(db, "MailSecurityIncidents"),
            "Worker-Opt-in ohne Schemafreigabe muss sicher abbrechen.");
    }

    public static async Task WorkerMayUseAlreadyApprovedSchemaAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Context();
        await MailSecuritySchemaMigrationPolicy.MigrateAsync(
            db, Config(schema: "true"));

        await MailSecuritySchemaMigrationPolicy.MigrateAsync(
            db, Config(worker: "true", schema: "false"));

        Assert(await HasTable(db, "MailSecurityIncidents"),
            "Worker darf eine bereits freigegebene Migration verwenden.");
    }

    public static async Task InvalidApprovalFlagDoesNotMigrateAsync()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Context();

        var threw = false;
        try
        {
            await MailSecuritySchemaMigrationPolicy.MigrateAsync(
                db, Config(schema: "yes"));
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert(threw && !await HasTable(db, "Installations"),
            "Ungueltige Freigabewerte duerfen keine Migration ausloesen.");
    }

    private static async Task<bool> HasTable(
        StatisticsDbContext db, string name)
    {
        var connection = db.Database.GetDbConnection();
        var mustClose = connection.State != System.Data.ConnectionState.Open;

        if (mustClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master " +
                "WHERE type = 'table' AND name = $name";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$name";
            parameter.Value = name;
            command.Parameters.Add(parameter);

            return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
        }
        finally
        {
            if (mustClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static Installation SyntheticInstallation() => new()
    {
        InstallationKey = "synthetic-installation",
        FirstSeenUtc = new DateTime(2026, 11, 1, 8, 0, 0,
            DateTimeKind.Utc),
        LastSeenUtc = new DateTime(2026, 11, 23, 8, 0, 0,
            DateTimeKind.Utc),
        CurrentVersion = "0.1.2",
        ConsentVersion = 1
    };

    private static IConfiguration Config(
        string? schema = null, string? worker = null)
    {
        var config = new Dictionary<string, string?>();
        if (schema is not null)
        {
            config[MailSecuritySchemaMigrationPolicy.AllowHistorySchemaMigrationKey]
                = schema;
        }

        if (worker is not null)
        {
            config[MailSecurityHistoryWorkerRegistration.EnableHistoryWorkerKey]
                = worker;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(config).Build();
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
            return new Fixture(connection);
        }

        public StatisticsDbContext Context() => new(_options);

        public async ValueTask DisposeAsync() =>
            await _connection.DisposeAsync();
    }
}
