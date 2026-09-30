using Microsoft.EntityFrameworkCore;
using Telenec.Mail.Statistics.Server.Data;
using Telenec.Mail.Statistics.Server.Security;

var builder = WebApplication.CreateBuilder(args);

var configuredDatabasePath = builder.Configuration["Statistics:DatabasePath"];

if (string.IsNullOrWhiteSpace(configuredDatabasePath))
{
    throw new InvalidOperationException(
        "Der Datenbankpfad 'Statistics:DatabasePath' wurde nicht konfiguriert.");
}

var databasePath = Path.IsPathRooted(configuredDatabasePath)
    ? configuredDatabasePath
    : Path.GetFullPath(
        configuredDatabasePath,
        builder.Environment.ContentRootPath);

var databaseDirectory = Path.GetDirectoryName(databasePath);

if (!string.IsNullOrWhiteSpace(databaseDirectory))
{
    Directory.CreateDirectory(databaseDirectory);
}

builder.Services.AddDbContext<StatisticsDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

builder.Services.AddSingleton<IInstallationKeyService, HmacInstallationKeyService>();

var app = builder.Build();

app.MapGet("/", () => "Telenec Mail Statistics Server");

app.Run();