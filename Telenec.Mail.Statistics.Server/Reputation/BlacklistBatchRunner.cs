
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;

namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Ein registrierter Blacklist-Prüfer.
///
/// Die Registrierung erfolgt ausschließlich
/// nach technischer und rechtlicher Freigabe
/// durch die aufrufende Komponente.
/// </summary>
public sealed record BlacklistCheckJob(
    string BlacklistId,
    Func<string, CancellationToken, Task<BlacklistCheckResult>>
        CheckAsync);

/// <summary>
/// Gemeinsamer Batch-Runner für IP-Blacklists.
///
/// Eigenschaften:
///
/// - Maximal vier tatsächlich laufende Prüfer
/// - Zeitbegrenzung pro Anbieter
/// - Zeitbegrenzung beim Warten auf einen Prüfplatz
/// - Fehlerisolierung pro Anbieter
/// - Prüfung der Ergebniskennungen und Zeitstempel
/// - Keine unbegrenzte Parallelisierung
///
/// Ein Prüfer, der einen Abbruch ignoriert,
/// darf den Batch nicht unbegrenzt blockieren.
///
/// Sein belegter Prüfplatz wird aber erst dann
/// freigegeben, wenn seine tatsächliche Ausführung
/// abgeschlossen ist.
///
/// Dadurch können hängende Prüfer keine
/// unbegrenzt vielen weiteren Prüfungen auslösen.
/// </summary>
public sealed class BlacklistBatchRunner
{
    private const int MaximumConcurrency = 4;

    private static readonly TimeSpan DefaultProviderTimeout =
        TimeSpan.FromSeconds(12);

    private readonly TimeSpan _providerTimeout;

    /*
     * Der Semaphore gehört zur Runner-Instanz.
     *
     * Er wird bewusst nicht für jeden Batch
     * neu erzeugt.
     *
     * Dadurch bleibt die Parallelitätsgrenze
     * auch über mehrere Prüfdurchläufe hinweg
     * wirksam.
     */
    private readonly SemaphoreSlim _executionSlots =
        new(MaximumConcurrency, MaximumConcurrency);

    public BlacklistBatchRunner(
        TimeSpan? providerTimeout = null)
    {
        _providerTimeout =
            providerTimeout ?? DefaultProviderTimeout;

        if (_providerTimeout <= TimeSpan.Zero
            || _providerTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerTimeout),
                "Das Anbieter-Zeitlimit muss größer als null " +
                "und höchstens fünf Minuten sein.");
        }
    }

    /// <summary>
    /// Führt alle registrierten Prüfer aus.
    ///
    /// Die Ergebnisse werden unter ihren
    /// Blacklist-Kennungen zurückgegeben.
    ///
    /// Ein fehlgeschlagener Anbieter verändert
    /// die anderen Ergebnisse nicht.
    /// </summary>
    public async Task<
        IReadOnlyDictionary<string, BlacklistCheckResult>>
        RunAsync(
            IReadOnlyCollection<BlacklistCheckJob> jobs,
            string ipv4Address,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        cancellationToken.ThrowIfCancellationRequested();

        if (!IPAddress.TryParse(
                ipv4Address,
                out var targetAddress)
            || targetAddress.AddressFamily
                != AddressFamily.InterNetwork)
        {
            throw new ArgumentException(
                "Das Prüfziel muss eine gültige IPv4-Adresse sein.",
                nameof(ipv4Address));
        }

        var registeredJobs = jobs.ToArray();

        /*
         * Alle Registrierungen vor dem Start prüfen.
         *
         * Doppelte oder unbekannte Blacklist-Kennungen
         * werden grundsätzlich abgewiesen.
         */

        var knownIds =
            BlacklistCatalog.Entries
                .Select(entry => entry.Id)
                .ToHashSet(StringComparer.Ordinal);

        var registeredIds =
            new HashSet<string>(StringComparer.Ordinal);

        foreach (var job in registeredJobs)
        {
            ArgumentNullException.ThrowIfNull(job);

            if (string.IsNullOrWhiteSpace(job.BlacklistId)
                || !knownIds.Contains(job.BlacklistId))
            {
                throw new InvalidOperationException(
                    "Nicht bekannte Blacklist-Kennung: " +
                    job.BlacklistId);
            }

            if (job.CheckAsync is null)
            {
                throw new InvalidOperationException(
                    "Kein Prüfer registriert für: " +
                    job.BlacklistId);
            }

            if (!registeredIds.Add(job.BlacklistId))
            {
                throw new InvalidOperationException(
                    "Blacklist mehrfach registriert: " +
                    job.BlacklistId);
            }
        }

        /*
         * Anbieter können parallel gestartet werden.
         *
         * Die tatsächliche maximale Parallelität
         * begrenzt RunJobAsync durch den gemeinsamen
         * Semaphore.
         */

        var tasks =
            registeredJobs
                .Select(job =>
                    RunJobAsync(
                        job,
                        ipv4Address,
                        cancellationToken))
                .ToArray();

        var completedResults =
            await Task.WhenAll(tasks);

        var dictionary =
            new Dictionary<string, BlacklistCheckResult>(
                StringComparer.Ordinal);

        foreach (var completed in completedResults)
        {
            dictionary.Add(
                completed.Key,
                completed.Value);
        }

        return new ReadOnlyDictionary<
            string,
            BlacklistCheckResult>(dictionary);
    }

    /// <summary>
    /// Führt einen einzelnen Anbieter aus.
    ///
    /// Das Warten auf einen freien Prüfplatz
    /// ist ebenfalls zeitlich begrenzt.
    ///
    /// Dies verhindert, dass ein dauerhaft
    /// hängender Anbieter spätere Batches
    /// unbegrenzt warten lässt.
    /// </summary>
    private async Task<
        KeyValuePair<string, BlacklistCheckResult>>
        RunJobAsync(
            BlacklistCheckJob job,
            string ipv4Address,
            CancellationToken cancellationToken)
    {
        var startedAtUtc =
            DateTimeOffset.UtcNow;

        BlacklistCheckResult Failure(
            string message)
        {
            return new BlacklistCheckResult
            {
                BlacklistId = job.BlacklistId,
                CheckedTarget = ipv4Address,
                Status = BlacklistCheckStatus.Failed,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ErrorMessage = message
            };
        }

        KeyValuePair<string, BlacklistCheckResult> Wrap(
            BlacklistCheckResult result)
        {
            return new KeyValuePair<
                string,
                BlacklistCheckResult>(
                job.BlacklistId,
                result);
        }

        /*
         * Auf freien Ausführungsplatz warten.
         *
         * Auch dieser Vorgang bekommt ein Zeitlimit.
         */

        using var slotWaitSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        slotWaitSource.CancelAfter(
            _providerTimeout);

        try
        {
            await _executionSlots.WaitAsync(
                slotWaitSource.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return Wrap(Failure(
                "Kein freier Prüfplatz innerhalb " +
                "des Zeitlimits verfügbar."));
        }

        /*
         * Nach erfolgreicher Platzreservierung
         * muss der Platz auf jeden Fall
         * wieder freigegeben werden.
         */

        if (cancellationToken.IsCancellationRequested)
        {
            _executionSlots.Release();

            cancellationToken.ThrowIfCancellationRequested();
        }

        /*
         * Separater Abbruch-Token für den Anbieter.
         *
         * Der Token bleibt gültig, solange
         * der Anbieter tatsächlich läuft.
         */

        var providerSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        providerSource.CancelAfter(
            _providerTimeout);

        /*
         * Die tatsächliche Anbieteroperation
         * wird als Task ausgeführt.
         *
         * Auch ein synchron blockierender Prüfer
         * soll nicht den aufrufenden Thread
         * des Monitoring-Dienstes blockieren.
         */

        var providerTask =
            Task.Run(
                async () =>
                    await job.CheckAsync(
                            ipv4Address,
                            providerSource.Token)
                        .ConfigureAwait(false),
                CancellationToken.None);

        /*
         * Der Prüfplatz wird erst freigegeben,
         * wenn der Anbieter tatsächlich fertig ist.
         *
         * Das gilt auch dann, wenn der Batch
         * wegen Timeout schon weitergelaufen ist.
         *
         * Fehler des zurückgelassenen Tasks
         * werden ebenfalls beobachtet.
         */

        _ = ObserveAndReleaseAsync(
            providerTask,
            providerSource);

        try
        {
            /*
             * WaitAsync beendet unser Warten,
             * wenn der Anbieter zu lange benötigt.
             *
             * Das gilt auch dann, wenn der
             * Anbieter seinen CancellationToken
             * selbst nicht beachtet.
             */

            var result =
                await providerTask.WaitAsync(
                    providerSource.Token);

            if (result is null)
            {
                return Wrap(Failure(
                    "Der Prüfer hat kein Ergebnis geliefert."));
            }

            if (result.BlacklistId != job.BlacklistId)
            {
                return Wrap(Failure(
                    "Der Prüfer hat eine falsche " +
                    "Blacklist-Kennung geliefert."));
            }

            if (result.CheckedTarget != ipv4Address)
            {
                return Wrap(Failure(
                    "Der Prüfer hat ein abweichendes " +
                    "Prüfziel geliefert."));
            }

            if (!Enum.IsDefined(result.Status))
            {
                return Wrap(Failure(
                    "Der Prüfer hat einen unbekannten " +
                    "Status geliefert."));
            }

            /*
             * Eindeutige Ergebnisse müssen
             * gültige und aktuelle Zeitangaben
             * enthalten.
             *
             * Veraltete Ergebnisse dürfen niemals
             * einen neuen grünen oder roten Status
             * erzeugen.
             */

            if (result.IsConclusive
                && (!result.StartedAtUtc.HasValue
                    || !result.CompletedAtUtc.HasValue
                    || result.StartedAtUtc.Value
                        > result.CompletedAtUtc.Value
                    || result.StartedAtUtc.Value
                        < startedAtUtc.AddSeconds(-5)
                    || result.CompletedAtUtc.Value
                        > DateTimeOffset.UtcNow.AddSeconds(5)))
            {
                return Wrap(Failure(
                    "Das Prüfergebnis besitzt ungültige " +
                    "oder veraltete Zeitangaben."));
            }

            return Wrap(result);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            /*
             * Ein ausdrücklich angeforderter
             * Programmabbruch wird weitergegeben.
             */
            throw;
        }
        catch (OperationCanceledException)
            when (providerSource.IsCancellationRequested)
        {
            return Wrap(Failure(
                "Der Blacklist-Anbieter hat das " +
                "Zeitlimit überschritten."));
        }
        catch (OperationCanceledException)
        {
            return Wrap(Failure(
                "Die Anbieterprüfung wurde abgebrochen."));
        }
        catch (Exception exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Wrap(Failure(
                "Die Anbieterprüfung ist fehlgeschlagen: " +
                exception.Message));
        }
    }

    /// <summary>
    /// Beobachtet die tatsächliche Beendigung
    /// eines Anbieter-Tasks.
    ///
    /// Ein bereits zurückgegebener Timeout
    /// bedeutet nicht zwingend, dass der
    /// Anbieter technisch beendet wurde.
    ///
    /// Der Ausführungsplatz bleibt deshalb
    /// bis zur tatsächlichen Beendigung belegt.
    ///
    /// Ein dauerhaft hängender Prüfer kann
    /// weiterhin einen Platz blockieren.
    /// Die Gesamtzahl dieser Plätze ist aber
    /// auf MaximumConcurrency begrenzt.
    /// </summary>
    private async Task ObserveAndReleaseAsync(
        Task<BlacklistCheckResult> providerTask,
        CancellationTokenSource providerSource)
    {
        try
        {
            await providerTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Der konkrete Anbieterfehler wird
            // durch RunJobAsync behandelt.
            //
            // Hier wird lediglich sichergestellt,
            // dass die Task-Exception beobachtet ist.
        }
        finally
        {
            providerSource.Dispose();

            _executionSlots.Release();
        }
    }
}
