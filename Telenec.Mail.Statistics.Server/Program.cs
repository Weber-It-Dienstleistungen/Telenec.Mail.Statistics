
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Telenec.Mail.Statistics.Server.Data;
using Telenec.Mail.Statistics.Server.Endpoints;
using Telenec.Mail.Statistics.Server.Options;
using Telenec.Mail.Statistics.Server.Reputation;
using Telenec.Mail.Statistics.Server.Security;
using Telenec.Mail.Statistics.Server.Services;
using Telenec.Mail.Statistics.Server.Validation;

var builder = WebApplication.CreateBuilder(args);

var configuredDatabasePath =
    builder.Configuration["Statistics:DatabasePath"];

if (string.IsNullOrWhiteSpace(configuredDatabasePath))
{
    throw new InvalidOperationException(
        "Der Datenbankpfad 'Statistics:DatabasePath' wurde nicht konfiguriert.");
}

var databasePath =
    Path.IsPathRooted(configuredDatabasePath)
        ? configuredDatabasePath
        : Path.GetFullPath(
            configuredDatabasePath,
            builder.Environment.ContentRootPath);

var databaseDirectory =
    Path.GetDirectoryName(databasePath);

if (!string.IsNullOrWhiteSpace(databaseDirectory))
{
    Directory.CreateDirectory(databaseDirectory);
}

builder.Services.Configure<ForwardedHeadersOptions>(
    options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto;

        /*
         * Der Statistikserver läuft hinter
         * lokalen Plesk-Reverse-Proxies.
         *
         * Nur bekannte lokale Proxies dürfen
         * Forwarded-Header liefern.
         */

        options.KnownProxies.Add(
            IPAddress.Loopback);

        options.KnownProxies.Add(
            IPAddress.IPv6Loopback);

        /*
         * Unterstützte Proxy-Ketten:
         *
         * nginx -> Kestrel
         * nginx -> Apache -> Kestrel
         */

        options.ForwardLimit = 2;
    });

builder.Services
    .AddOptions<StatisticsRetentionOptions>()
    .Bind(
        builder.Configuration.GetSection(
            StatisticsRetentionOptions.SectionName))
    .Validate(
        options =>
            options.RetentionDays is >= 1 and <= 3650,
        "Statistics:RetentionDays muss zwischen 1 und 3650 Tagen liegen.")
    .ValidateOnStart();

builder.Services.AddDbContext<StatisticsDbContext>(
    options =>
        options.UseSqlite(
            $"Data Source={databasePath}"));

builder.Services.AddSingleton<
    IInstallationKeyService,
    HmacInstallationKeyService>();

builder.Services.AddSingleton<
    IAdminApiKeyValidator,
    AdminApiKeyValidator>();

builder.Services.AddSingleton<
    IUsageRequestValidator,
    UsageRequestValidator>();

builder.Services.AddSingleton<TimeProvider>(
    TimeProvider.System);

builder.Services.AddScoped<
    IUsageStatisticsService,
    UsageStatisticsService>();

builder.Services.AddScoped<
    IStatisticsQueryService,
    StatisticsQueryService>();

/*
 * Manueller Blacklist-Check.
 *
 * Der Dienst ist ein Singleton, damit alle
 * Admin-Anfragen denselben Prüfzustand sehen
 * und dieselbe Parallelitätssperre verwenden.
 *
 * Keine Registrierung als Hosted Service:
 * Beim Serverstart werden keine DNSBL-Abfragen
 * automatisch ausgeführt.
 *
 * GET liest den letzten Zustand.
 * POST startet eine neue Prüfung.
 */

builder.Services.AddSingleton<
    BlacklistMonitoringService>();

/*
 * Bestehende Server-Hintergrunddienste.
 *
 * Datenbankinitialisierung und Aufbewahrungs-
 * regelung bleiben unverändert.
 */

builder.Services.AddHostedService<
    ServerInitializationHostedService>();

builder.Services.AddHostedService<
    StatisticsRetentionHostedService>();

builder.Services.AddRateLimiter(
    options =>
    {
        options.RejectionStatusCode =
            StatusCodes.Status429TooManyRequests;

        /*
         * Bestehende Nutzungsstatistik:
         *
         * Maximal 120 Anfragen pro Minute
         * und Client-IP.
         *
         * Diese Regel bleibt unverändert.
         */

        options.AddPolicy(
            UsageEndpoints.RateLimitPolicyName,
            httpContext =>
            {
                var partitionKey =
                    httpContext
                        .Connection
                        .RemoteIpAddress?
                        .ToString()
                    ?? "unknown";

                return RateLimitPartition
                    .GetFixedWindowLimiter(
                        partitionKey,
                        _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 120,
                                Window =
                                    TimeSpan.FromMinutes(1),
                                QueueLimit = 0,
                                AutoReplenishment = true
                            });
            });

        /*
         * Manueller Blacklist-Check.
         *
         * Gültige und ungültige Admin-Schlüssel
         * erhalten getrennte Rate-Limit-Partitionen.
         *
         * Dadurch können fehlgeschlagene
         * Authentifizierungsversuche nicht das
         * Kontingent berechtigter Anfragen
         * derselben Client-IP verbrauchen.
         *
         * Die eigentliche Authentifizierung
         * bleibt im Admin-Endpoint bestehen.
         */

        options.AddPolicy(
            AdminEndpoints.BlacklistCheckRateLimitPolicyName,
            httpContext =>
            {
                var clientIp =
                    httpContext
                        .Connection
                        .RemoteIpAddress?
                        .ToString()
                    ?? "unknown";

                var apiKeyValidator =
                    httpContext.RequestServices
                        .GetRequiredService<
                            IAdminApiKeyValidator>();

                var providedApiKey =
                    httpContext.Request.Headers[
                            "X-Admin-Key"]
                        .FirstOrDefault();

                var isAuthorized =
                    apiKeyValidator.IsValid(
                        providedApiKey);

                /*
                 * Getrennte Partitionen:
                 *
                 * authorized:<IP>
                 * unauthorized:<IP>
                 *
                 * Der API-Schlüssel selbst wird
                 * niemals als Partitionskennung
                 * gespeichert.
                 */

                var partitionKey =
                    (isAuthorized
                        ? "authorized:"
                        : "unauthorized:")
                    + clientIp;

                /*
                 * Berechtigte Anfragen:
                 * 2 Prüfversuche pro Minute.
                 *
                 * Unberechtigte Anfragen:
                 * 10 Versuche pro Minute.
                 *
                 * Beide Kontingente werden
                 * unabhängig voneinander geführt.
                 */

                var permitLimit =
                    isAuthorized ? 2 : 10;

                return RateLimitPartition
                    .GetFixedWindowLimiter(
                        partitionKey,
                        _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = permitLimit,
                                Window =
                                    TimeSpan.FromMinutes(1),
                                QueueLimit = 0,
                                AutoReplenishment = true
                            });
            });
    });

var app = builder.Build();

/*
 * Forwarded-Header müssen vor dem Rate-Limiter
 * verarbeitet werden, damit RemoteIpAddress
 * die tatsächliche Client-IP enthält.
 */

app.UseForwardedHeaders();

app.UseRateLimiter();

app.MapGet(
    "/",
    () => "Telenec Mail Statistics Server");

app.MapUsageEndpoints();

app.MapAdminEndpoints();

app.Run();
