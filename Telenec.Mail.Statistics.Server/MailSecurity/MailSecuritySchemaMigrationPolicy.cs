using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Telenec.Mail.Statistics.Server.Data;

namespace Telenec.Mail.Statistics.Server.MailSecurity;

/// <summary>
/// Sprint 43.4f: Sichert die bestehende Nutzungsstatistik gegen eine
/// unbeabsichtigte Anlage der accountbezogenen Security-Historie ab.
/// Die neue History-Migration wird NUR nach expliziter Freigabe
/// eingespielt. Bereits installierte Migrationen werden niemals
/// auf einen aelteren Stand zurueckgesetzt.
/// </summary>
public static class MailSecuritySchemaMigrationPolicy
{
    public const string InitialMigration =
        "20260930113558_InitialCreate";

    public const string HistoryMigration =
        "20261123070000_AddMailSecurityHistory";

    public const string AllowHistorySchemaMigrationKey =
        "MailSecurity:AllowHistorySchemaMigration";

    public static async Task MigrateAsync(
        StatisticsDbContext database,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(configuration);

        var allowHistory = StrictBoolean(
            configuration[AllowHistorySchemaMigrationKey],
            AllowHistorySchemaMigrationKey);

        var workerEnabled = StrictBoolean(
            configuration[MailSecurityHistoryWorkerRegistration.EnableHistoryWorkerKey],
            MailSecurityHistoryWorkerRegistration.EnableHistoryWorkerKey);

        // Migrationskatalog explizit absichern: Neue EF-Core-Migrationen
        // erfordern eine bewusste Aktualisierung dieser Freigabepolitik.
        // So darf ein zukuenftiger Code-Stand nicht stillschweigend
        // weitere Schemata installieren.
        var available = (await database.Database.GetMigrationsAsync(
            cancellationToken)).ToArray();

        if (!available.SequenceEqual(
                new[] { InitialMigration, HistoryMigration },
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Unbekannter EF-Core-Migrationsstand. " +
                "Migrationsfreigabe vor Serverstart aktualisieren.");
        }

        var applied = (await database.Database.GetAppliedMigrationsAsync(
            cancellationToken)).ToHashSet(StringComparer.Ordinal);

        if (applied.Any(m => !available.Contains(m, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                "Die Datenbank enthaelt unbekannte Migrationen. " +
                "Keine automatische Schemaaenderung durchfuehren.");
        }

        if (!allowHistory)
        {
            // Der Worker darf niemals auf nicht vorhandene History-
            // Tabellen zugreifen. Vor einem expliziten Opt-in gilt
            // diese Kombination als Konfigurationsfehler.
            if (workerEnabled && !applied.Contains(HistoryMigration))
            {
                throw new InvalidOperationException(
                    "Mail-Security-Worker aktiviert, aber History-Schema " +
                    "fehlt und ist nicht zur Migration freigegeben.");
            }

            if (applied.Contains(InitialMigration))
            {
                // Wichtig: NICHT MigrateAsync(InitialMigration) auf
                // einer bereits weiter migrierten Datenbank aufrufen!
                // Das waere eine Rueckmigration mit Tabellenverlust.
                return;
            }

            if (applied.Count != 0)
            {
                throw new InvalidOperationException(
                    "Ungueltige EF-Core-Migrationsreihenfolge.");
            }

            // Neuinstallation: Nur die bewährte Installationsstatistik
            // anlegen. Die History-Migration bleibt aus.
            await database.Database.MigrateAsync(
                InitialMigration, cancellationToken);
            return;
        }

        // Schemafreigabe und Workeraktivierung sind UNABHAENGIG:
        // Vor einem Workerstart darf das History-Schema bewusst
        // schon angelegt und geprueft werden.
        await database.Database.MigrateAsync(cancellationToken);
    }

    private static bool StrictBoolean(string? value, string key)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)
            || string.Equals(value, "false",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(value, "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new InvalidOperationException(
            key + " muss explizit 'true' oder 'false' sein.");
    }
}
