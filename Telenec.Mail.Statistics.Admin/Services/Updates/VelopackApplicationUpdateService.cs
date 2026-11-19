using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace Telenec.Mail.Statistics.Admin.Services.Updates;

public sealed class VelopackApplicationUpdateService
    : IApplicationUpdateService
{
    // Permanenter, oeffentlich erreichbarer HTTPS-Updatefeed auf dem
    // Telenec-Server. Die Admin-API-Adresse und ihre Zugangsdaten sind
    // davon vollstaendig getrennt.
    private const string UpdateFeedUrl =
        "https://dav.necnet.de/updates/statistics-admin/";

    private static readonly TimeSpan UpdateCheckTimeout =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan UpdateDownloadTimeout =
        TimeSpan.FromMinutes(10);

    public async Task<bool> TryApplyAvailableUpdateAsync(
        Action<string?>? statusChanged = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Bestehende Installationen erhalten diese Umstellung einmalig
            // ueber ein letztes GitHub-Release. Danach kommen alle neuen
            // Versionen ausschliesslich vom Telenec-HTTPS-Feed.
            var source =
                new SimpleWebSource(UpdateFeedUrl);

            var updateManager =
                new UpdateManager(source);

            /*
             * Ein normaler Entwicklungs- oder Publish-Start
             * außerhalb einer Velopack-Installation darf niemals
             * versuchen, Updates einzuspielen.
             */
            if (!updateManager.IsInstalled)
            {
                return false;
            }

            statusChanged?.Invoke(
                "Suche nach Updates …");

            var updateInfo =
                await updateManager
                    .CheckForUpdatesAsync()
                    .WaitAsync(
                        UpdateCheckTimeout,
                        cancellationToken);

            if (updateInfo is null)
            {
                statusChanged?.Invoke(null);

                return false;
            }

            statusChanged?.Invoke(
                $"Update {updateInfo.TargetFullRelease.Version} " +
                "wird heruntergeladen …");

            using var downloadCancellation =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);

            downloadCancellation.CancelAfter(
                UpdateDownloadTimeout);

            await updateManager
                .DownloadUpdatesAsync(
                    updateInfo,
                    progress =>
                    {
                        statusChanged?.Invoke(
                            $"Update wird heruntergeladen … {progress}%");
                    },
                    downloadCancellation.Token);

            statusChanged?.Invoke(
                "Update wird installiert …");

            updateManager.ApplyUpdatesAndRestart(
                updateInfo);

            return true;
        }
        catch (OperationCanceledException exception)
        {
            Trace.WriteLine(
                $"Telenec Mail Statistics update cancelled: {exception}");

            statusChanged?.Invoke(null);

            return false;
        }
        catch (TimeoutException exception)
        {
            Trace.WriteLine(
                $"Telenec Mail Statistics update check timed out: {exception}");

            statusChanged?.Invoke(null);

            return false;
        }
        catch (Exception exception)
        {
            /*
             * Ein Updateproblem darf niemals verhindern,
             * dass MiPo die Statistik öffnen kann.
             */
            Trace.WriteLine(
                $"Telenec Mail Statistics update failed: {exception}");

            statusChanged?.Invoke(null);

            return false;
        }
    }
}