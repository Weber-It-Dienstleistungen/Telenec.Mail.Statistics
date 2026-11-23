using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telenec.Mail.Statistics.Server.MailSecurity;

namespace Telenec.Mail.Statistics.Tests;

/// <summary>
/// Sprint 43.4e: testet nur Dienstregistrierung und Konfiguration.
/// Startet keinen Server, Timer oder SQLite-Worker.
/// </summary>
internal static class MailSecurityHistoryWorkerRegistrationTests
{
    public static Task AbsentFlagKeepsWorkerDisabledAsync()
    {
        var services = new ServiceCollection();
        services.AddMailSecurityHistoryWorkerIfEnabled(Config());

        Assert(RegisteredWorkers(services) == 0,
            "Ohne explizites Opt-in darf der Worker nicht registriert sein.");
        return Task.CompletedTask;
    }

    public static Task FalseFlagKeepsWorkerDisabledAsync()
    {
        var services = new ServiceCollection();
        services.AddMailSecurityHistoryWorkerIfEnabled(
            Config(flag: "false"));

        Assert(RegisteredWorkers(services) == 0,
            "EnableHistoryWorker=false muss deaktiviert bleiben.");
        return Task.CompletedTask;
    }

    public static Task TrueWithSnapshotRegistersOnceAsync()
    {
        var services = new ServiceCollection();
        var config = Config(
            flag: " TrUe ", path: "/synthetic/plesk-snapshot.json");

        services.AddMailSecurityHistoryWorkerIfEnabled(config);
        services.AddMailSecurityHistoryWorkerIfEnabled(config);

        Assert(RegisteredWorkers(services) == 1,
            "Explizites Opt-in darf genau einen Worker registrieren.");

        return Task.CompletedTask;
    }

    public static Task TrueWithoutSnapshotFailsClosedAsync()
    {
        var services = new ServiceCollection();
        var failed = false;

        try
        {
            services.AddMailSecurityHistoryWorkerIfEnabled(
                Config(flag: "true"));
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }

        Assert(failed && RegisteredWorkers(services) == 0,
            "Opt-in ohne SnapshotPath muss abbrechen, ohne Worker zu starten.");
        return Task.CompletedTask;
    }

    public static Task InvalidFlagsFailClosedAsync()
    {
        foreach (var flag in new[] { "yes", "1", "maybe" })
        {
            var services = new ServiceCollection();
            var failed = false;

            try
            {
                services.AddMailSecurityHistoryWorkerIfEnabled(
                    Config(flag: flag,
                        path: "/synthetic/plesk-snapshot.json"));
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }

            Assert(failed && RegisteredWorkers(services) == 0,
                $"Ungueltiger Konfigurationswert {flag} darf nicht aktivieren.");
        }

        return Task.CompletedTask;
    }

    private static IConfiguration Config(
        string? flag = null, string? path = null)
    {
        var values = new Dictionary<string, string?>();

        if (flag is not null)
        {
            values.Add(
                MailSecurityHistoryWorkerRegistration.EnableHistoryWorkerKey,
                flag);
        }

        if (path is not null)
        {
            values.Add(
                MailSecurityHistoryWorkerRegistration.SnapshotPathKey,
                path);
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static int RegisteredWorkers(IServiceCollection services) =>
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType ==
                typeof(MailSecurityHistoryWorker));

    private static void Assert(bool valid, string message)
    {
        if (!valid)
        {
            throw new InvalidOperationException(message);
        }
    }
}
