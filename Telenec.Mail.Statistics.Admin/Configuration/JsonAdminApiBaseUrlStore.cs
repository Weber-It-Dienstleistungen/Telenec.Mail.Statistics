using System.IO;
using System.Text.Json;

namespace Telenec.Mail.Statistics.Admin.Configuration;

public sealed class JsonAdminApiBaseUrlStore : IAdminApiBaseUrlStore
{
    private readonly string _settingsFilePath;

    public JsonAdminApiBaseUrlStore()
    {
        var localApplicationData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        var settingsDirectory =
            Path.Combine(
                localApplicationData,
                "Telenec",
                "Mail Statistics",
                "Admin");

        _settingsFilePath =
            Path.Combine(
                settingsDirectory,
                "settings.json");
    }

    public async Task<string?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return null;
        }

        await using var stream =
            new FileStream(
                _settingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

        var settings =
            await JsonSerializer.DeserializeAsync<StoredSettings>(
                stream,
                cancellationToken: cancellationToken);

        return string.IsNullOrWhiteSpace(settings?.BaseUrl)
            ? null
            : settings.BaseUrl;
    }

    public async Task SaveAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var directory =
            Path.GetDirectoryName(
                _settingsFilePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "Das Verzeichnis für die Admin-Konfiguration konnte nicht bestimmt werden.");
        }

        Directory.CreateDirectory(directory);

        var temporaryFilePath =
            _settingsFilePath + ".tmp";

        var settings =
            new StoredSettings
            {
                BaseUrl = baseUrl
            };

        try
        {
            await using (var stream =
                new FileStream(
                    temporaryFilePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    cancellationToken: cancellationToken);

                await stream.FlushAsync(
                    cancellationToken);
            }

            File.Move(
                temporaryFilePath,
                _settingsFilePath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFilePath))
            {
                File.Delete(temporaryFilePath);
            }
        }
    }

    public Task DeleteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(_settingsFilePath))
        {
            File.Delete(_settingsFilePath);
        }

        return Task.CompletedTask;
    }

    private sealed class StoredSettings
    {
        public string BaseUrl { get; set; } =
            string.Empty;
    }
}