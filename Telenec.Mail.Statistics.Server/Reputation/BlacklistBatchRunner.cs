
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;

namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Ein ausdrücklich registrierter Blacklist-Prüfer.
///
/// Die Registrierung bestätigt noch keine
/// Anbieterfreigabe. Nur zuvor geprüfte und
/// freigegebene Prüfer dürfen dem produktiven
/// Batch übergeben werden.
/// </summary>
public sealed record BlacklistCheckJob(
    string BlacklistId,
    Func<string, CancellationToken, Task<BlacklistCheckResult>>
        CheckAsync);

/// <summary>
/// Führt mehrere unabhängige Blacklist-Prüfungen
/// innerhalb eines gemeinsamen Durchlaufs aus.
///
/// Eigenschaften:
///
/// - Maximal vier gleichzeitige Prüfungen
/// - Eigenes Zeitlimit je Anbieter
/// - Fehlerisolierung pro Anbieter
/// - Keine stillschweigenden Erfolgsmeldungen
/// - Eine Ergebnisliste für das Monitoring
///
/// Der Runner führt keine Abfragen aus,
/// solange RunAsync nicht aufgerufen wird.
/// </summary>
public sealed class BlacklistBatchRunner
{
    private const int MaximumConcurrency = 4;

    private static readonly TimeSpan ProviderTimeout =
        TimeSpan.FromSeconds(12);

    /// <summary>
    /// Startet einen kontrollierten Prüfdurchlauf
    /// für alle übergebenen Anbieter.
    ///
    /// Einzelne Providerfehler unterbrechen
    /// nicht die übrigen Prüfungen.
    ///
    /// Ein regulärer Programmabbruch wird
    /// dagegen an den Aufrufer weitergereicht.
    /// </summary>
    public async Task<
        IReadOnlyDictionary<string, BlacklistCheckResult>>
        RunAsync(
            IReadOnlyCollection<BlacklistCheckJob> jobs,
            string ipv4Address,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);

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

        // Vor dem Start alle Registrierungen prüfen.
        // Ungültige oder doppelte Kennungen dürfen
        // nicht unbemerkt ausgeführt werden.

        var knownIds = BlacklistCatalog.Entries
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

        using var semaphore =
            new SemaphoreSlim(MaximumConcurrency);

        var tasks = registeredJobs
            .Select(async job =>
            {
                await semaphore.WaitAsync(
                    cancellationToken);

                try
                {
                    var result =
                        await ExecuteProviderAsync(
                            job,
                            ipv4Address,
                            cancellationToken);

                    return new KeyValuePair<
                        string,
                        BlacklistCheckResult>(
                            job.BlacklistId,
                            result);
                }
                finally
                {
                    semaphore.Release();
                }
            })
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
    /// Führt eine Anbieterprüfung mit einem
    /// eigenen Zeitlimit aus.
    ///
    /// Technische Fehler werden als Failed
    /// zurückgegeben, statt den gesamten
    /// Batch zum Scheitern zu bringen.
    /// </summary>
    private static async Task<BlacklistCheckResult>
        ExecuteProviderAsync(
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

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutSource.CancelAfter(ProviderTimeout);

        try
        {
            var result =
                await job.CheckAsync(
                    ipv4Address,
                    timeoutSource.Token);

            if (result is null)
            {
                return Failure(
                    "Der Prüfer hat kein Ergebnis geliefert.");
            }

            if (result.BlacklistId != job.BlacklistId)
            {
                return Failure(
                    "Der Prüfer hat eine falsche Blacklist-Kennung geliefert.");
            }

            if (result.CheckedTarget != ipv4Address)
            {
                return Failure(
                    "Der Prüfer hat ein abweichendes Prüfziel geliefert.");
            }

            if (!Enum.IsDefined(result.Status))
            {
                return Failure(
                    "Der Prüfer hat einen unbekannten Status geliefert.");
            }

            // Ein eindeutiges Ergebnis muss
            // über plausible Zeitangaben verfügen.
            //
            // Andernfalls darf kein grüner
            // oder roter Status entstehen.

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
                return Failure(
                    "Das Prüfergebnis besitzt ungültige oder veraltete Zeitangaben.");
            }

            return result;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                "Der Blacklist-Anbieter hat das Zeitlimit überschritten.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Failure(
                "Die Anbieterprüfung ist fehlgeschlagen: " +
                exception.Message);
        }
    }
}
