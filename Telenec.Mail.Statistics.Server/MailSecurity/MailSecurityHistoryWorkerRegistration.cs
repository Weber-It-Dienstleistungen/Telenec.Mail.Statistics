using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Telenec.Mail.Statistics.Server.MailSecurity;

/// <summary>
/// Explizite, fail-closed Registrierung fuer die beobachtende
/// Mail-Security-Historie. Ohne genaues "true" ist kein
/// Hintergrundworker registriert; ungueltige Werte brechen
/// die Initialisierung ab statt stillschweigend zu aktivieren.
/// </summary>
public static class MailSecurityHistoryWorkerRegistration
{
    public const string EnableHistoryWorkerKey =
        "MailSecurity:EnableHistoryWorker";

    public const string SnapshotPathKey =
        "MailSecurity:SnapshotPath";

    public static IServiceCollection AddMailSecurityHistoryWorkerIfEnabled(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredValue =
            configuration[EnableHistoryWorkerKey]?.Trim();

        if (string.IsNullOrEmpty(configuredValue)
            || string.Equals(configuredValue, "false",
                StringComparison.OrdinalIgnoreCase))
        {
            return services;
        }

        if (!string.Equals(configuredValue, "true",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "MailSecurity:EnableHistoryWorker muss explizit " +
                "'true' oder 'false' sein.");
        }

        // Kein Worker ohne konfigurierte lokale Datenquelle.
        // Eine zum Startzeitpunkt noch fehlende Snapshot-Datei
        // wird im bestehenden Reader sicher als unavailable behandelt.
        if (string.IsNullOrWhiteSpace(
                configuration[SnapshotPathKey]))
        {
            throw new InvalidOperationException(
                "MailSecurity:SnapshotPath muss vor Aktivierung " +
                "der Mail-Security-Historie konfiguriert sein.");
        }

        services.AddHostedService<MailSecurityHistoryWorker>();
        return services;
    }
}
