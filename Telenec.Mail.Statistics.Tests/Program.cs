
using System.Net;
using Telenec.Mail.Statistics.Server.Reputation;

namespace Telenec.Mail.Statistics.Tests;

internal static class Program
{
    private const string PositiveQuery =
        "2.0.0.127.test.example";

    private const string NegativeQuery =
        "1.0.0.127.test.example";

    private const string TargetQuery =
        "10.2.0.192.test.example";

    private const string TestIpAddress = "192.0.2.10";

    private static readonly DnsBlacklistDnsResponse NxDomain =
        new(true, Array.Empty<IPAddress>());

    private static async Task<int> Main()
    {
        Console.WriteLine("Telenec DNSBL – Regressionstests");
        Console.WriteLine();

        var passed = 0;
        var failed = 0;

        var scenarios = new[]
        {
            new Scenario(
                "Nicht gelistet",
                Addresses("127.0.0.2"),
                NxDomain,
                NxDomain,
                BlacklistCheckStatus.NotListed,
                false,
                3),

            new Scenario(
                "Gelistet",
                Addresses("127.0.0.2"),
                NxDomain,
                Addresses("127.0.0.2"),
                BlacklistCheckStatus.Listed,
                true,
                3),

            new Scenario(
                "Zugriff verweigert",
                Addresses("127.0.0.2"),
                NxDomain,
                Addresses("127.255.255.254"),
                BlacklistCheckStatus.AccessDenied,
                null,
                3),

            new Scenario(
                "Unbekannter Antwortcode",
                Addresses("127.0.0.2"),
                NxDomain,
                Addresses("127.0.0.99"),
                BlacklistCheckStatus.Unknown,
                null,
                3),

            new Scenario(
                "Gemischte Antwortcodes",
                Addresses("127.0.0.2"),
                NxDomain,
                Addresses("127.0.0.2", "127.0.0.99"),
                BlacklistCheckStatus.Unknown,
                null,
                3),

            new Scenario(
                "Leere Anbieterantwort",
                Addresses("127.0.0.2"),
                NxDomain,
                Addresses(),
                BlacklistCheckStatus.Unknown,
                null,
                3),

            new Scenario(
                "Positive Kontrolle fehlgeschlagen",
                NxDomain,
                null,
                null,
                BlacklistCheckStatus.Failed,
                null,
                1),

            new Scenario(
                "Negative Kontrolle fehlgeschlagen",
                Addresses("127.0.0.2"),
                Addresses("127.0.0.2"),
                null,
                BlacklistCheckStatus.Failed,
                null,
                2),

            new Scenario(
                "Zugriff bei Kontrollabfrage verweigert",
                Addresses("127.255.255.254"),
                null,
                null,
                BlacklistCheckStatus.AccessDenied,
                null,
                1),

            new Scenario(
                "Mehrdeutige Positivkontrolle",
                Addresses("127.0.0.2", "127.0.0.99"),
                null,
                null,
                BlacklistCheckStatus.Failed,
                null,
                1)
        };

        foreach (var scenario in scenarios)
        {
            await RunTestAsync(
                scenario.Name,
                () => RunScenarioAsync(scenario));
        }

        await RunTestAsync(
            "Ungültiges Prüfziel",
            CheckInvalidAddressAsync);

        Console.WriteLine();
        Console.WriteLine(
            "Blacklist-Batch – Mehrfachanbieter-Tests");
        Console.WriteLine();

        await RunTestAsync(
            "Mehrere Anbieter in einem Durchlauf",
            CheckMultipleProvidersAsync);

        await RunTestAsync(
            "Anbieterfehler bleiben isoliert",
            CheckProviderFailureIsolationAsync);

        await RunTestAsync(
            "Leeres Prüfergebnis wird abgewiesen",
            CheckNullResultAsync);

        await RunTestAsync(
            "Falsche Blacklist-Kennung wird abgewiesen",
            CheckWrongBlacklistIdAsync);

        await RunTestAsync(
            "Abweichendes Prüfziel wird abgewiesen",
            CheckWrongTargetAsync);

        await RunTestAsync(
            "Ungültige Prüfzeitpunkte werden abgewiesen",
            CheckMissingTimestampsAsync);

        await RunTestAsync(
            "Doppelte Anbieterregistrierung wird abgewiesen",
            CheckDuplicateRegistrationAsync);

        await RunTestAsync(
            "Unbekannter Anbieter wird abgewiesen",
            CheckUnknownProviderAsync);

        await RunTestAsync(
            "Ungültige Batch-IP wird abgewiesen",
            CheckInvalidBatchAddressAsync);

        await RunTestAsync(
            "Programmabbruch wird weitergegeben",
            CheckBatchCancellationAsync);

        Console.WriteLine();
        Console.WriteLine(
            "Blacklist-Batch – Timeout- und Lasttests");
        Console.WriteLine();

        await RunTestAsync(
            "Ignorierter Abbruch führt zu Timeout",
            CheckIgnoredCancellationTimeoutAsync);

        await RunTestAsync(
            "Maximal vier laufende Anbieter",
            CheckMaximumConcurrencyAsync);

        await RunTestAsync(
            "Abbruch während hängender Prüfung",
            CheckCancellationDuringPendingCheckAsync);

        Console.WriteLine();

        Console.WriteLine(
            $"Ergebnis: {passed} erfolgreich, {failed} fehlgeschlagen.");

        return failed == 0 ? 0 : 1;

        async Task RunTestAsync(
            string name,
            Func<Task> test)
        {
            try
            {
                await test();

                Console.WriteLine($"[OK] {name}");
                passed++;
            }
            catch (Exception exception)
            {
                Console.WriteLine($"[FEHLER] {name}");
                Console.WriteLine($"         {exception.Message}");
                failed++;
            }
        }
    }

    // ===================================================
    // DNSBL-Engine – bisherige Regressionstests
    // ===================================================

    private static async Task RunScenarioAsync(
        Scenario scenario)
    {
        var answers =
            new Dictionary<string, DnsBlacklistDnsResponse>(
                StringComparer.OrdinalIgnoreCase);

        if (scenario.PositiveResponse is not null)
            answers.Add(PositiveQuery, scenario.PositiveResponse);

        if (scenario.NegativeResponse is not null)
            answers.Add(NegativeQuery, scenario.NegativeResponse);

        if (scenario.TargetResponse is not null)
            answers.Add(TargetQuery, scenario.TargetResponse);

        var resolver = new FakeDnsResolver(answers);

        var checker = new DnsBlacklistChecker(
            CreateProfile(),
            resolver);

        var result = await checker.CheckAsync(TestIpAddress);

        Assert(
            result.Status == scenario.ExpectedStatus,
            $"Status erwartet: {scenario.ExpectedStatus}, " +
            $"erhalten: {result.Status}");

        Assert(
            result.IsListed == scenario.ExpectedIsListed,
            "IsListed stimmt nicht mit dem erwarteten Zustand überein.");

        var expectedConclusive =
            scenario.ExpectedStatus is
                BlacklistCheckStatus.Listed
                or BlacklistCheckStatus.NotListed;

        Assert(
            result.IsConclusive == expectedConclusive,
            "IsConclusive ist fachlich nicht korrekt.");

        Assert(
            result.BlacklistId == "test-dnsbl",
            "Die Blacklist-Kennung ist nicht korrekt.");

        Assert(
            result.CheckedTarget == TestIpAddress,
            "Das geprüfte Ziel ist nicht korrekt.");

        Assert(
            result.StartedAtUtc.HasValue
            && result.CompletedAtUtc.HasValue,
            "Prüfzeitpunkte fehlen.");

        Assert(
            resolver.Queries.Count == scenario.ExpectedQueryCount,
            "Die Anzahl der DNS-Abfragen ist falsch.");

        var expectedQueries =
            new[]
            {
                PositiveQuery,
                NegativeQuery,
                TargetQuery
            }
            .Take(scenario.ExpectedQueryCount);

        Assert(
            resolver.Queries.SequenceEqual(
                expectedQueries,
                StringComparer.OrdinalIgnoreCase),
            "Die DNS-Abfragen wurden nicht in der erwarteten Reihenfolge durchgeführt.");
    }

    private static async Task CheckInvalidAddressAsync()
    {
        var resolver = new FakeDnsResolver(
            new Dictionary<string, DnsBlacklistDnsResponse>());

        var checker = new DnsBlacklistChecker(
            CreateProfile(),
            resolver);

        var result = await checker.CheckAsync(
            "keine-ip-adresse");

        Assert(
            result.Status == BlacklistCheckStatus.Failed,
            "Ein ungültiges Prüfziel wurde nicht als Fehler bewertet.");

        Assert(
            !result.IsConclusive && result.IsListed is null,
            "Ein ungültiges Prüfziel wurde als verlässliches Ergebnis bewertet.");

        Assert(
            resolver.Queries.Count == 0,
            "Bei ungültigem Prüfziel wurde trotzdem DNS abgefragt.");
    }

    // ===================================================
    // Batch-Runner – bestehende Regressionstests
    // ===================================================

    private static async Task CheckMultipleProvidersAsync()
    {
        var runner = new BlacklistBatchRunner();

        var jobs = new[]
        {
            CreateJob(
                "psbl",
                BlacklistCheckStatus.NotListed),

            CreateJob(
                "spamcop",
                BlacklistCheckStatus.Listed),

            CreateJob(
                "blocklist-de",
                BlacklistCheckStatus.AccessDenied)
        };

        var results =
            await runner.RunAsync(jobs, TestIpAddress);

        Assert(results.Count == 3,
            "Es wurden nicht alle drei Ergebnisse geliefert.");

        Assert(
            results["psbl"].Status == BlacklistCheckStatus.NotListed,
            "Das PSBL-Ergebnis ist falsch.");

        Assert(
            results["psbl"].IsListed == false,
            "PSBL wurde nicht als negativer Befund bewertet.");

        Assert(
            results["spamcop"].Status == BlacklistCheckStatus.Listed,
            "Das simulierte SpamCop-Ergebnis ist falsch.");

        Assert(
            results["spamcop"].IsListed == true,
            "Der simulierte Listungstreffer wurde nicht erkannt.");

        Assert(
            results["blocklist-de"].Status ==
                BlacklistCheckStatus.AccessDenied,
            "Die simulierte Zugriffssperre wurde nicht erkannt.");

        Assert(
            results["blocklist-de"].IsListed is null,
            "Die Zugriffssperre wurde als Listungsergebnis bewertet.");
    }

    private static async Task CheckProviderFailureIsolationAsync()
    {
        var runner = new BlacklistBatchRunner();

        var jobs = new[]
        {
            CreateJob(
                "psbl",
                BlacklistCheckStatus.NotListed),

            new BlacklistCheckJob(
                "spamcop",
                (_, _) => throw new InvalidOperationException(
                    "Simulierter Anbieterausfall"))
        };

        var results =
            await runner.RunAsync(jobs, TestIpAddress);

        Assert(results.Count == 2,
            "Der Batch hat nicht beide Ergebnisse geliefert.");

        Assert(
            results["psbl"].Status == BlacklistCheckStatus.NotListed,
            "Der funktionierende Anbieter wurde beeinträchtigt.");

        Assert(
            results["spamcop"].Status == BlacklistCheckStatus.Failed,
            "Der Anbieterausfall wurde nicht als Fehler erfasst.");

        Assert(
            !results["spamcop"].IsConclusive
            && results["spamcop"].IsListed is null,
            "Der Anbieterausfall wurde als verlässlicher Befund bewertet.");
    }

    private static async Task CheckNullResultAsync()
    {
        var runner = new BlacklistBatchRunner();

        var jobs = new[]
        {
            new BlacklistCheckJob(
                "psbl",
                (_, _) => Task.FromResult<BlacklistCheckResult>(
                    null!))
        };

        var results =
            await runner.RunAsync(jobs, TestIpAddress);

        Assert(
            results["psbl"].Status == BlacklistCheckStatus.Failed,
            "Ein leeres Ergebnis wurde nicht abgewiesen.");
    }

    private static async Task CheckWrongBlacklistIdAsync()
    {
        var runner = new BlacklistBatchRunner();

        var results = await runner.RunAsync(
            new[]
            {
                CreateJob(
                    "psbl",
                    BlacklistCheckStatus.NotListed,
                    actualId: "spamcop")
            },
            TestIpAddress);

        Assert(
            results["psbl"].Status == BlacklistCheckStatus.Failed,
            "Eine falsche Blacklist-Kennung wurde akzeptiert.");
    }

    private static async Task CheckWrongTargetAsync()
    {
        var runner = new BlacklistBatchRunner();

        var results = await runner.RunAsync(
            new[]
            {
                CreateJob(
                    "psbl",
                    BlacklistCheckStatus.NotListed,
                    actualTarget: "192.0.2.99")
            },
            TestIpAddress);

        Assert(
            results["psbl"].Status == BlacklistCheckStatus.Failed,
            "Ein abweichendes Prüfziel wurde akzeptiert.");
    }

    private static async Task CheckMissingTimestampsAsync()
    {
        var runner = new BlacklistBatchRunner();

        var results = await runner.RunAsync(
            new[]
            {
                CreateJob(
                    "psbl",
                    BlacklistCheckStatus.NotListed,
                    includeTimestamps: false)
            },
            TestIpAddress);

        Assert(
            results["psbl"].Status == BlacklistCheckStatus.Failed,
            "Ein Prüfergebnis ohne Zeitstempel wurde akzeptiert.");

        Assert(
            results["psbl"].IsListed is null,
            "Ein fehlerhaftes Prüfergebnis wurde als negativ bewertet.");
    }

    private static async Task CheckDuplicateRegistrationAsync()
    {
        var runner = new BlacklistBatchRunner();

        await ExpectExceptionAsync<InvalidOperationException>(
            () => runner.RunAsync(
                new[]
                {
                    CreateJob(
                        "psbl",
                        BlacklistCheckStatus.NotListed),

                    CreateJob(
                        "psbl",
                        BlacklistCheckStatus.NotListed)
                },
                TestIpAddress));
    }

    private static async Task CheckUnknownProviderAsync()
    {
        var runner = new BlacklistBatchRunner();

        await ExpectExceptionAsync<InvalidOperationException>(
            () => runner.RunAsync(
                new[]
                {
                    CreateJob(
                        "anbieter-existiert-nicht",
                        BlacklistCheckStatus.NotListed)
                },
                TestIpAddress));
    }

    private static async Task CheckInvalidBatchAddressAsync()
    {
        var runner = new BlacklistBatchRunner();

        await ExpectExceptionAsync<ArgumentException>(
            () => runner.RunAsync(
                new[]
                {
                    CreateJob(
                        "psbl",
                        BlacklistCheckStatus.NotListed)
                },
                "keine-ip-adresse"));
    }

    private static async Task CheckBatchCancellationAsync()
    {
        var runner = new BlacklistBatchRunner();

        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await ExpectExceptionAsync<OperationCanceledException>(
            () => runner.RunAsync(
                new[]
                {
                    CreateJob(
                        "psbl",
                        BlacklistCheckStatus.NotListed)
                },
                TestIpAddress,
                cancellation.Token));
    }

    // ===================================================
    // NEU: Timeout- und Parallelitätsprüfung
    // ===================================================

    /// <summary>
    /// Ein Anbieter ignoriert die Abbruchanforderung.
    ///
    /// Der Batch muss trotzdem nach seinem
    /// Zeitlimit mit Failed zurückkehren.
    ///
    /// Das simulierte Hängen wird anschließend
    /// im finally-Block aufgelöst.
    /// </summary>
    private static async Task CheckIgnoredCancellationTimeoutAsync()
    {
        var runner = new BlacklistBatchRunner(
            TimeSpan.FromMilliseconds(200));

        var pending =
            new TaskCompletionSource<BlacklistCheckResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var results = await runner.RunAsync(
                    new[]
                    {
                        new BlacklistCheckJob(
                            "psbl",
                            (_, _) => pending.Task)
                    },
                    TestIpAddress)
                .WaitAsync(TimeSpan.FromSeconds(5));

            var result = results["psbl"];

            Assert(
                result.Status == BlacklistCheckStatus.Failed,
                "Der hängende Anbieter wurde nicht als Fehler bewertet.");

            Assert(
                !result.IsConclusive && result.IsListed is null,
                "Der Timeout wurde als verlässliches Ergebnis bewertet.");

            Assert(
                result.ErrorMessage?.Contains(
                    "Zeitlimit",
                    StringComparison.OrdinalIgnoreCase) == true,
                "Die Timeout-Meldung fehlt.");
        }
        finally
        {
            pending.TrySetResult(CreateResult(
                "psbl",
                TestIpAddress,
                BlacklistCheckStatus.NotListed));
        }
    }

    /// <summary>
    /// Fünf getrennte Batches verwenden
    /// dieselbe Runner-Instanz.
    ///
    /// Vier Anbieter dürfen gleichzeitig
    /// tatsächlich laufen.
    ///
    /// Der fünfte muss wegen des belegten
    /// Prüfplatzes scheitern.
    ///
    /// Dadurch überprüfen wir die Begrenzung
    /// auch über mehrere Batch-Aufrufe hinweg.
    /// </summary>
    private static async Task CheckMaximumConcurrencyAsync()
    {
        var runner = new BlacklistBatchRunner(
            TimeSpan.FromSeconds(2));

        var pending =
            new TaskCompletionSource<BlacklistCheckResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var startedCount = 0;

        try
        {
            var runs = Enumerable.Range(0, 5)
                .Select(_ => runner.RunAsync(
                    new[]
                    {
                        new BlacklistCheckJob(
                            "psbl",
                            (_, _) =>
                            {
                                Interlocked.Increment(
                                    ref startedCount);

                                // Absichtlich ignorierter Token.
                                return pending.Task;
                            })
                    },
                    TestIpAddress))
                .ToArray();

            var results = await Task.WhenAll(runs)
                .WaitAsync(TimeSpan.FromSeconds(8));

            Assert(
                Volatile.Read(ref startedCount) == 4,
                "Die Parallelitätsgrenze wurde nicht korrekt eingehalten.");

            Assert(
                results.All(item =>
                    item["psbl"].Status ==
                    BlacklistCheckStatus.Failed),
                "Nicht alle blockierten Anbieter wurden als Fehler bewertet.");

            var unavailableSlots = results.Count(item =>
                item["psbl"].ErrorMessage?.Contains(
                    "Kein freier Prüfplatz",
                    StringComparison.OrdinalIgnoreCase) == true);

            Assert(
                unavailableSlots == 1,
                "Ein belegter Prüfplatz wurde nicht korrekt erkannt.");
        }
        finally
        {
            pending.TrySetResult(CreateResult(
                "psbl",
                TestIpAddress,
                BlacklistCheckStatus.NotListed));
        }
    }

    /// <summary>
    /// Der Benutzer bzw. Host beendet
    /// einen laufenden Batch ausdrücklich.
    ///
    /// Auch wenn ein Prüfer noch hängt,
    /// muss der Abbruch weitergegeben werden.
    /// </summary>
    private static async Task CheckCancellationDuringPendingCheckAsync()
    {
        var runner = new BlacklistBatchRunner(
            TimeSpan.FromSeconds(3));

        using var cancellation =
            new CancellationTokenSource();

        var pending =
            new TaskCompletionSource<BlacklistCheckResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var started =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var batchTask = runner.RunAsync(
                new[]
                {
                    new BlacklistCheckJob(
                        "psbl",
                        (_, _) =>
                        {
                            started.TrySetResult();

                            // Prüfer ignoriert den Abbruch.
                            return pending.Task;
                        })
                },
                TestIpAddress,
                cancellation.Token);

            await started.Task.WaitAsync(
                TimeSpan.FromSeconds(5));

            cancellation.Cancel();

            await ExpectExceptionAsync<OperationCanceledException>(
                () => batchTask.WaitAsync(
                    TimeSpan.FromSeconds(5)));
        }
        finally
        {
            pending.TrySetResult(CreateResult(
                "psbl",
                TestIpAddress,
                BlacklistCheckStatus.NotListed));
        }
    }

    // ===================================================
    // Gemeinsame Hilfsfunktionen
    // ===================================================

    private static BlacklistCheckJob CreateJob(
        string blacklistId,
        BlacklistCheckStatus status,
        string? actualId = null,
        string? actualTarget = null,
        bool includeTimestamps = true)
    {
        return new BlacklistCheckJob(
            blacklistId,
            (target, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var now = DateTimeOffset.UtcNow;

                var result = new BlacklistCheckResult
                {
                    BlacklistId =
                        actualId ?? blacklistId,

                    CheckedTarget =
                        actualTarget ?? target,

                    Status = status,

                    StartedAtUtc =
                        includeTimestamps ? now : null,

                    CompletedAtUtc =
                        includeTimestamps ? now : null
                };

                return Task.FromResult(result);
            });
    }

    private static BlacklistCheckResult CreateResult(
        string blacklistId,
        string target,
        BlacklistCheckStatus status)
    {
        var now = DateTimeOffset.UtcNow;

        return new BlacklistCheckResult
        {
            BlacklistId = blacklistId,
            CheckedTarget = target,
            Status = status,
            StartedAtUtc = now,
            CompletedAtUtc = now
        };
    }

    private static async Task ExpectExceptionAsync<TException>(
        Func<Task> operation)
        where TException : Exception
    {
        try
        {
            await operation();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Die erwartete Ausnahme " +
            $"{typeof(TException).Name} wurde nicht ausgelöst.");
    }

    private static DnsBlacklistProfile CreateProfile()
    {
        return new DnsBlacklistProfile(
            BlacklistId: "test-dnsbl",
            DnsZone: "test.example",
            ListedReturnCodes:
                new[] { "127.0.0.2" },
            AccessDeniedReturnCodes:
                new[] { "127.255.255.254" },
            PositiveControlAddress: "127.0.0.2",
            NegativeControlAddress: "127.0.0.1");
    }

    private static DnsBlacklistDnsResponse Addresses(
        params string[] addresses)
    {
        return new DnsBlacklistDnsResponse(
            IsNxDomain: false,
            Addresses: addresses
                .Select(IPAddress.Parse)
                .ToArray());
    }

    private static void Assert(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record Scenario(
        string Name,
        DnsBlacklistDnsResponse? PositiveResponse,
        DnsBlacklistDnsResponse? NegativeResponse,
        DnsBlacklistDnsResponse? TargetResponse,
        BlacklistCheckStatus ExpectedStatus,
        bool? ExpectedIsListed,
        int ExpectedQueryCount);

    private sealed class FakeDnsResolver
        : IDnsBlacklistResolver
    {
        private readonly
            IReadOnlyDictionary<string, DnsBlacklistDnsResponse>
                _answers;

        public List<string> Queries { get; } = new();

        public FakeDnsResolver(
            IReadOnlyDictionary<
                string,
                DnsBlacklistDnsResponse> answers)
        {
            _answers = answers;
        }

        public Task<DnsBlacklistDnsResponse> QueryAsync(
            string queryName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Queries.Add(queryName);

            if (!_answers.TryGetValue(
                    queryName,
                    out var answer))
            {
                throw new InvalidOperationException(
                    $"Keine simulierte DNS-Antwort für {queryName} vorhanden.");
            }

            return Task.FromResult(answer);
        }
    }
}
