using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Telenec.Mail.Statistics.Server.Data;
using Telenec.Mail.Statistics.Server.MailSecurity;
using Telenec.Mail.Statistics.Server.Models;

namespace Telenec.Mail.Statistics.Tests;

/// <summary>
/// Sprint 43.4g: echte temporaere SQLite-Dateien, Online-Backup,
/// Upgrade einer Kopie und getrennter Restore. Keine Serverdaten.
/// </summary>
internal static class MailSecurityFileUpgradeTests
{
    public static async Task BackupUpgradeAndIsolatedRestoreAsync()
    {
        await using var fixture = new FileFixture();

        // Ausgangslage wie auf dem vorhandenen Statistikserver:
        // ausschliesslich die alte Installationsmigration und
        // bereits gespeicherte Nutzungsstatistik.
        await using (var original = fixture.Open(fixture.OriginalPath))
        {
            await original.Database.MigrateAsync(
                MailSecuritySchemaMigrationPolicy.InitialMigration);

            original.Installations.AddRange(
                Installation("synthetic-installation-a", "0.1.2"),
                Installation("synthetic-installation-b", "0.1.3"));
            await original.SaveChangesAsync();
        }

        // Gleicher Backup-Mechanismus wie der Linux-Backupdienst:
        // Microsoft.Data.Sqlite nutzt die SQLite Online Backup API.
        // Quelle bleibt dabei die alte, funktionierende Datenbank.
        using (var source = new SqliteConnection(
            FileFixture.ConnectionString(
                fixture.OriginalPath, SqliteOpenMode.ReadOnly)))
        using (var destination = new SqliteConnection(
            FileFixture.ConnectionString(
                fixture.BackupPath, SqliteOpenMode.ReadWriteCreate)))
        {
            await source.OpenAsync();
            await destination.OpenAsync();
            source.BackupDatabase(destination);
        }

        Assert(await IntegrityOkAsync(fixture.BackupPath),
            "Die konsistente SQLite-Sicherung muss integrity_check=ok ergeben.");

        // Migration absichtlich nur auf einer separaten ARBEITSKOPIE.
        File.Copy(fixture.BackupPath, fixture.UpgradePath);

        await using (var upgraded = fixture.Open(fixture.UpgradePath))
        {
            // Ohne ausdrueckliches Opt-in darf die Kopie nicht migrieren.
            await MailSecuritySchemaMigrationPolicy.MigrateAsync(
                upgraded, Config(schema: "false"));
            Assert(!await HasTableAsync(upgraded, "MailSecurityIncidents"),
                "Default-Start darf die History-Tabelle nicht anlegen.");

            // Jetzt Migration bewusst freigeben, weiterhin ohne Worker.
            await MailSecuritySchemaMigrationPolicy.MigrateAsync(
                upgraded, Config(schema: "true", worker: "false"));

            Assert(await HasTableAsync(upgraded, "MailSecurityIncidents")
                   && await HasTableAsync(
                       upgraded, "MailSecurityCheckpointStates"),
                "Das Opt-in muss beide History-Tabellen anlegen.");

            await AssertInstallationsAsync(upgraded);
            Assert((await upgraded.Database.GetAppliedMigrationsAsync())
                   .Contains(MailSecuritySchemaMigrationPolicy.HistoryMigration),
                "Die Migration muss im EF-Core-Protokoll stehen.");
        }

        // Der Ausgangsstand und die Online-Sicherung muessen
        // unveraendert bleiben, obwohl die Kopie migriert wurde.
        await using (var original = fixture.Open(fixture.OriginalPath))
        {
            Assert(!await HasTableAsync(original, "MailSecurityIncidents"),
                "Die originale Testdatenbank darf nicht mitmigriert werden.");
            await AssertInstallationsAsync(original);
        }

        // Isolierter RESTORE aus der geprueften Sicherungsdatei.
        File.Copy(fixture.BackupPath, fixture.RestoredPath);
        await using (var restored = fixture.Open(fixture.RestoredPath))
        {
            Assert(await IntegrityOkAsync(fixture.RestoredPath),
                "Die wiederhergestellte Datenbank muss integer sein.");
            await AssertInstallationsAsync(restored);
            Assert(!await HasTableAsync(restored, "MailSecurityIncidents"),
                "Restore muss den Zustand VOR der Migration enthalten.");

            // Der Server muss nach dem Restore mit Default-Flags
            // ohne ungewollte History-Migration wiederanlaufen.
            await MailSecuritySchemaMigrationPolicy.MigrateAsync(
                restored, Config(schema: "false"));
            await AssertInstallationsAsync(restored);
            Assert(!await HasTableAsync(restored, "MailSecurityIncidents"),
                "Wiederanlauf mit Default-Flags darf nicht migrieren.");
        }
    }

    public static async Task UnknownMigrationFailsWithoutChangingFileAsync()
    {
        await using var fixture = new FileFixture();
        await using var db = fixture.Open(fixture.OriginalPath);

        await db.Database.MigrateAsync(
            MailSecuritySchemaMigrationPolicy.InitialMigration);
        db.Installations.Add(
            Installation("synthetic-installation-a", "0.1.2"));
        await db.SaveChangesAsync();

        // Simuliert eine Datenbank, deren Versionsverzeichnis
        // nicht zum gestarteten Code passt.
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"__EFMigrationsHistory\" " +
            "(\"MigrationId\", \"ProductVersion\") " +
            "VALUES ('20990101000000_Unknown', '10.0.12')");

        var refused = false;
        try
        {
            await MailSecuritySchemaMigrationPolicy.MigrateAsync(
                db, Config(schema: "true"));
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        Assert(refused,
            "Unbekannte Migrationen muessen vor Schreibzugriff stoppen.");
        Assert(!await HasTableAsync(db, "MailSecurityIncidents"),
            "Bei unbekanntem Stand darf keine neue Tabelle entstehen.");
        Assert(await db.Installations.CountAsync() == 1,
            "Vorhandene Installation muss erhalten bleiben.");
    }

    private static Installation Installation(string key, string version) =>
        new()
        {
            InstallationKey = key,
            FirstSeenUtc = new DateTime(2026, 11, 1, 0, 0, 0,
                DateTimeKind.Utc),
            LastSeenUtc = new DateTime(2026, 11, 23, 8, 0, 0,
                DateTimeKind.Utc),
            CurrentVersion = version,
            ConsentVersion = 1
        };

    private static IConfiguration Config(
        string schema, string worker = "false") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [MailSecuritySchemaMigrationPolicy.AllowHistorySchemaMigrationKey]
                    = schema,
                [MailSecurityHistoryWorkerRegistration.EnableHistoryWorkerKey]
                    = worker
            }).Build();

    private static async Task AssertInstallationsAsync(
        StatisticsDbContext context)
    {
        var entries = await context.Installations.AsNoTracking()
            .OrderBy(x => x.InstallationKey)
            .ToArrayAsync();

        Assert(entries.Length == 2
               && entries[0].InstallationKey == "synthetic-installation-a"
               && entries[0].CurrentVersion == "0.1.2"
               && entries[1].InstallationKey == "synthetic-installation-b"
               && entries[1].CurrentVersion == "0.1.3",
            "Beide synthetischen Bestandsinstallationen "
            + "muessen unveraendert erhalten bleiben.");
    }

    private static async Task<bool> IntegrityOkAsync(string path)
    {
        await using var connection = new SqliteConnection(
            FileFixture.ConnectionString(path, SqliteOpenMode.ReadOnly));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        return string.Equals(
            (string?)await command.ExecuteScalarAsync(),
            "ok", StringComparison.Ordinal);
    }

    private static async Task<bool> HasTableAsync(
        StatisticsDbContext context, string tableName)
    {
        var connection = context.Database.GetDbConnection();
        var closeAfter = connection.State !=
            System.Data.ConnectionState.Open;
        if (closeAfter)
        {
            await connection.OpenAsync();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM sqlite_master " +
                "WHERE type='table' AND name=$table";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$table";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);
            return Convert.ToInt64(
                await command.ExecuteScalarAsync()) == 1;
        }
        finally
        {
            if (closeAfter)
            {
                await connection.CloseAsync();
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

    private sealed class FileFixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "telenec-upgrade-" + Guid.NewGuid().ToString("N"));

        public FileFixture()
        {
            Directory.CreateDirectory(_directory);
        }

        public string OriginalPath => Path.Combine(_directory, "original.db");
        public string BackupPath => Path.Combine(_directory, "backup.db");
        public string UpgradePath => Path.Combine(_directory, "upgrade.db");
        public string RestoredPath => Path.Combine(_directory, "restored.db");

        public StatisticsDbContext Open(string path)
        {
            var options = new DbContextOptionsBuilder<StatisticsDbContext>()
                .UseSqlite(ConnectionString(path, SqliteOpenMode.ReadWriteCreate))
                .Options;
            return new StatisticsDbContext(options);
        }

        public static string ConnectionString(
            string path, SqliteOpenMode mode) =>
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = mode,
                Pooling = false
            }.ToString();

        public ValueTask DisposeAsync()
        {
            // Saemtliche SQLite-Verbindungen werden vor dem
            // Aufruf bereits geschlossen. Nur Testdateien loeschen.
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
