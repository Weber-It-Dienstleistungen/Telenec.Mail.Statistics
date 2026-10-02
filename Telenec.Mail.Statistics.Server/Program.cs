using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Telenec.Mail.Statistics.Server.Data;
using Telenec.Mail.Statistics.Server.Endpoints;
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
    Directory.CreateDirectory(
        databaseDirectory);
}

builder.Services.Configure<ForwardedHeadersOptions>(
    options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto;

        /*
         * Der Statistikserver wird ausschließlich hinter
         * lokalen Plesk-Webservern betrieben.
         *
         * Nur Reverse Proxies auf diesem Server dürfen
         * Forwarded-Header liefern. Externe Clients können
         * dadurch RemoteIpAddress nicht über manipulierte
         * X-Forwarded-For-Header beeinflussen.
         */
        options.KnownProxies.Add(
            IPAddress.Loopback);

        options.KnownProxies.Add(
            IPAddress.IPv6Loopback);

        /*
         * Unterstützt sowohl:
         *
         * nginx -> Kestrel
         *
         * als auch:
         *
         * nginx -> Apache -> Kestrel
         */
        options.ForwardLimit = 2;
    });

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

builder.Services.AddHostedService<
    ServerInitializationHostedService>();

builder.Services.AddRateLimiter(
    options =>
    {
        options.RejectionStatusCode =
            StatusCodes.Status429TooManyRequests;

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
    });

var app =
    builder.Build();

/*
 * Muss vor dem Rate-Limiter ausgeführt werden,
 * damit RemoteIpAddress bereits die tatsächliche
 * Client-IP enthält.
 */
app.UseForwardedHeaders();

app.UseRateLimiter();

app.MapGet(
    "/",
    () => "Telenec Mail Statistics Server");

app.MapUsageEndpoints();
app.MapAdminEndpoints();

app.Run();