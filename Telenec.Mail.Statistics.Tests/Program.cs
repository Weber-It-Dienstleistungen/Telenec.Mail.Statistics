
using System.Net;
using Telenec.Mail.Statistics.Server.Reputation;

namespace Telenec.Mail.Statistics.Tests;

internal static class Program
{
    private const string TestIp = "192.0.2.10";
    private const string PositiveQuery = "2.0.0.127.test.example";
    private const string NegativeQuery = "1.0.0.127.test.example";
    private const string TargetQuery = "10.2.0.192.test.example";

    private static readonly DnsBlacklistDnsResponse NxDomain =
        new(true, Array.Empty<IPAddress>());

    private static readonly ProfileExpectation[] ExpectedProfiles =
    {
        new("0spam", "bl.0spam.org", "127.0.0.2"),
        new("0spam-rbl", "rbl.0spam.org", "127.0.0.2"),
        new("drone-bl", "dnsbl.dronebl.org", "127.0.0.1"),
        new("uceprotectl1", "dnsbl-1.uceprotect.net", "127.0.0.2")
    };

    private static async Task<int> Main()
    {
        var passed = 0;
        var failed = 0;

        async Task Test(string name, Func<Task> action)
        {
            try
            {
                await action();
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

        Console.WriteLine("Telenec DNSBL – Regressionstests");
        Console.WriteLine();

        var scenarios = new[]
        {
            new Scenario("Nicht gelistet",
                Addresses("127.0.0.2"), NxDomain, NxDomain,
                BlacklistCheckStatus.NotListed, false, 3),

            new Scenario("Gelistet",
                Addresses("127.0.0.2"), NxDomain,
                Addresses("127.0.0.2"),
                BlacklistCheckStatus.Listed, true, 3),

            new Scenario("Zugriff verweigert",
                Addresses("127.0.0.2"), NxDomain,
                Addresses("127.255.255.254"),
                BlacklistCheckStatus.AccessDenied, null, 3),

            new Scenario("Unbekannter Antwortcode",
                Addresses("127.0.0.2"), NxDomain,
                Addresses("127.0.0.99"),
                BlacklistCheckStatus.Unknown, null, 3),

            new Scenario("Gemischte Antwortcodes",
                Addresses("127.0.0.2"), NxDomain,
                Addresses("127.0.0.2", "127.0.0.99"),
                BlacklistCheckStatus.Unknown, null, 3),

            new Scenario("Leere Anbieterantwort",
                Addresses("127.0.0.2"), NxDomain, Addresses(),
                BlacklistCheckStatus.Unknown, null, 3),

            new Scenario("Positive Kontrolle fehlgeschlagen",
                NxDomain, null, null,
                BlacklistCheckStatus.Failed, null, 1),

            new Scenario("Negative Kontrolle fehlgeschlagen",
                Addresses("127.0.0.2"),
                Addresses("127.0.0.2"), null,
                BlacklistCheckStatus.Failed, null, 2),

            new Scenario("Zugriff bei Kontrollabfrage verweigert",
                Addresses("127.255.255.254"), null, null,
                BlacklistCheckStatus.AccessDenied, null, 1),

            new Scenario("Mehrdeutige Positivkontrolle",
                Addresses("127.0.0.2", "127.0.0.99"),
                null, null,
                BlacklistCheckStatus.Failed, null, 1)
        };

        foreach (var scenario in scenarios)
        {
            await Test(
                scenario.Name,
                () => RunDnsScenarioAsync(scenario));
        }

        await Test("Ungültiges Prüfziel", CheckInvalidAddressAsync);

        Console.WriteLine();
        Console.WriteLine("Blacklist-Batch – Mehrfachanbieter-Tests");
        Console.WriteLine();

        await Test("Mehrere Anbieter in einem Durchlauf",
            CheckMultipleProvidersAsync);

        await Test("Anbieterfehler bleiben isoliert",
            CheckFailureIsolationAsync);

        await Test("Leeres Prüfergebnis wird abgewiesen",
            CheckNullResultAsync);

        await Test("Falsche Blacklist-Kennung wird abgewiesen",
            CheckWrongIdAsync);

        await Test("Abweichendes Prüfziel wird abgewiesen",
            CheckWrongTargetAsync);

        await Test("Ungültige Prüfzeitpunkte werden abgewiesen",
            CheckMissingTimestampsAsync);

        await Test("Doppelte Anbieterregistrierung wird abgewiesen",
            CheckDuplicateRegistrationAsync);

        await Test("Unbekannter Anbieter wird abgewiesen",
            CheckUnknownProviderAsync);

        await Test("Ungültige Batch-IP wird abgewiesen",
            CheckInvalidBatchAddressAsync);

        await Test("Programmabbruch wird weitergegeben",
            CheckBatchCancellationAsync);

        Console.WriteLine();
        Console.WriteLine("Blacklist-Batch – Timeout- und Lasttests");
        Console.WriteLine();

        await Test("Ignorierter Abbruch führt zu Timeout",
            CheckIgnoredCancellationTimeoutAsync);

        await Test("Maximal vier laufende Anbieter",
            CheckMaximumConcurrencyAsync);

        await Test("Abbruch während hängender Prüfung",
            CheckCancellationDuringPendingCheckAsync);

        Console.WriteLine();
        Console.WriteLine("DNSBL-Anbieterprofile – Erste Gruppe");
        Console.WriteLine();

        await Test("Vier eindeutige, gültige Anbieterprofile",
            CheckProviderDefinitionsAsync);

        await Test("Vier Anbieter: Nicht gelistet",
            () => CheckProviderScenariosAsync(
                ProfileTestMode.NotListed));

        await Test("Vier Anbieter: Gelistet",
            () => CheckProviderScenariosAsync(
                ProfileTestMode.Listed));

        await Test("Vier Anbieter: Unbekannter Antwortcode",
            () => CheckProviderScenariosAsync(
                ProfileTestMode.Unknown));

        await Test("Vier Anbieter: Defekte Positivkontrolle",
            () => CheckProviderScenariosAsync(
                ProfileTestMode.FailedControl));

        Console.WriteLine();
        Console.WriteLine(
            $"Ergebnis: {passed} erfolgreich, {failed} fehlgeschlagen.");

        return failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------
    // Bestehende DNSBL-Tests
    // ---------------------------------------------------

    private static async Task RunDnsScenarioAsync(Scenario scenario)
    {
        var answers = new Dictionary<string, DnsBlacklistDnsResponse>();

        if (scenario.Positive is not null)
            answers.Add(PositiveQuery, scenario.Positive);

        if (scenario.Negative is not null)
            answers.Add(NegativeQuery, scenario.Negative);

        if (scenario.Target is not null)
            answers.Add(TargetQuery, scenario.Target);

        var resolver = new FakeResolver(answers);
        var checker = new DnsBlacklistChecker(
            CreateTestProfile(), resolver);

        var result = await checker.CheckAsync(TestIp);

        Assert(result.Status == scenario.Status,
            $"Erwartet {scenario.Status}, erhalten {result.Status}");

        Assert(result.IsListed == scenario.IsListed,
            "IsListed ist falsch.");

        Assert(result.IsConclusive ==
            (scenario.Status is BlacklistCheckStatus.Listed
                or BlacklistCheckStatus.NotListed),
            "IsConclusive ist falsch.");

        Assert(result.BlacklistId == "test-dnsbl",
            "Blacklist-Kennung ist falsch.");

        Assert(result.CheckedTarget == TestIp,
            "Prüfziel ist falsch.");

        Assert(result.StartedAtUtc.HasValue &&
            result.CompletedAtUtc.HasValue,
            "Zeitstempel fehlen.");

        Assert(resolver.Queries.Count == scenario.QueryCount,
            "Anzahl der DNS-Abfragen ist falsch.");

        Assert(resolver.Queries.SequenceEqual(
                new[] { PositiveQuery, NegativeQuery, TargetQuery }
                    .Take(scenario.QueryCount)),
            "DNS-Abfragen wurden in falscher Reihenfolge ausgeführt.");
    }

    private static async Task CheckInvalidAddressAsync()
    {
        var resolver = new FakeResolver(
            new Dictionary<string, DnsBlacklistDnsResponse>());

        var checker = new DnsBlacklistChecker(
            CreateTestProfile(), resolver);

        var result = await checker.CheckAsync("keine-ip-adresse");

        Assert(result.Status == BlacklistCheckStatus.Failed,
            "Ungültige IP wurde nicht abgewiesen.");

        Assert(!result.IsConclusive && result.IsListed is null,
            "Ungültige IP wurde als belastbarer Befund bewertet.");

        Assert(resolver.Queries.Count == 0,
            "Trotz ungültiger IP wurde DNS abgefragt.");
    }

    // ---------------------------------------------------
    // Bestehende Batch-Regressionstests
    // ---------------------------------------------------

    private static async Task CheckMultipleProvidersAsync()
    {
        var results = await new BlacklistBatchRunner().RunAsync(
            new[]
            {
                Job("psbl", BlacklistCheckStatus.NotListed),
                Job("spamcop", BlacklistCheckStatus.Listed),
                Job("blocklist-de", BlacklistCheckStatus.AccessDenied)
            },
            TestIp);

        Assert(results.Count == 3, "Es fehlen Ergebnisse.");

        Assert(results["psbl"].Status ==
            BlacklistCheckStatus.NotListed &&
            results["psbl"].IsListed == false,
            "PSBL ist falsch.");

        Assert(results["spamcop"].Status ==
            BlacklistCheckStatus.Listed &&
            results["spamcop"].IsListed == true,
            "Simulierter Listungstreffer ist falsch.");

        Assert(results["blocklist-de"].Status ==
            BlacklistCheckStatus.AccessDenied &&
            results["blocklist-de"].IsListed is null,
            "Zugriffssperre ist falsch.");
    }

    private static async Task CheckFailureIsolationAsync()
    {
        var results = await new BlacklistBatchRunner().RunAsync(
            new[]
            {
                Job("psbl", BlacklistCheckStatus.NotListed),
                new BlacklistCheckJob(
                    "spamcop",
                    (_, _) => throw new InvalidOperationException(
                        "Simulierter Ausfall"))
            },
            TestIp);

        Assert(results.Count == 2, "Ein Ergebnis fehlt.");

        Assert(results["psbl"].Status ==
            BlacklistCheckStatus.NotListed,
            "Funktionierender Anbieter wurde beeinträchtigt.");

        Assert(results["spamcop"].Status ==
            BlacklistCheckStatus.Failed &&
            !results["spamcop"].IsConclusive &&
            results["spamcop"].IsListed is null,
            "Anbieterausfall wurde falsch interpretiert.");
    }

    private static async Task CheckNullResultAsync()
    {
        var results = await new BlacklistBatchRunner().RunAsync(
            new[]
            {
                new BlacklistCheckJob(
                    "psbl",
                    (_, _) => Task.FromResult<BlacklistCheckResult>(
                        null!))
            },
            TestIp);

        Assert(results["psbl"].Status ==
            BlacklistCheckStatus.Failed,
            "Null-Ergebnis wurde akzeptiert.");
    }

    private static async Task CheckWrongIdAsync()
    {
        var results = await new BlacklistBatchRunner().RunAsync(
            new[]
            {
                Job("psbl", BlacklistCheckStatus.NotListed,
                    actualId: "spamcop")
            },
            TestIp);

        Assert(results["psbl"].Status ==
            BlacklistCheckStatus.Failed,
            "Falsche Blacklist-Kennung wurde akzeptiert.");
    }

    private static async Task CheckWrongTargetAsync()
    {
        var results = await new BlacklistBatchRunner().RunAsync(
            new[]
            {
                Job("psbl", BlacklistCheckStatus.NotListed,
                    actualTarget: "192.0.2.99")
            },
            TestIp);

        Assert(results["psbl"].Status ==
            BlacklistCheckStatus.Failed,
            "Falsches Prüfziel wurde akzeptiert.");
    }

    private static async Task CheckMissingTimestampsAsync()
    {
        var results = await new BlacklistBatchRunner().RunAsync(
            new[]
            {
                Job("psbl", BlacklistCheckStatus.NotListed,
                    withTimestamps: false)
            },
            TestIp);

        Assert(results["psbl"].Status ==
            BlacklistCheckStatus.Failed &&
            results["psbl"].IsListed is null,
            "Ergebnis ohne Zeitstempel wurde akzeptiert.");
    }

    private static async Task CheckDuplicateRegistrationAsync()
    {
        await ExpectAsync<InvalidOperationException>(
            () => new BlacklistBatchRunner().RunAsync(
                new[]
                {
                    Job("psbl", BlacklistCheckStatus.NotListed),
                    Job("psbl", BlacklistCheckStatus.NotListed)
                },
                TestIp));
    }

    private static async Task CheckUnknownProviderAsync()
    {
        await ExpectAsync<InvalidOperationException>(
            () => new BlacklistBatchRunner().RunAsync(
                new[]
                {
                    Job("anbieter-existiert-nicht",
                        BlacklistCheckStatus.NotListed)
                },
                TestIp));
    }

    private static async Task CheckInvalidBatchAddressAsync()
    {
        await ExpectAsync<ArgumentException>(
            () => new BlacklistBatchRunner().RunAsync(
                new[]
                {
                    Job("psbl", BlacklistCheckStatus.NotListed)
                },
                "keine-ip-adresse"));
    }

    private static async Task CheckBatchCancellationAsync()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        await ExpectAsync<OperationCanceledException>(
            () => new BlacklistBatchRunner().RunAsync(
                new[]
                {
                    Job("psbl", BlacklistCheckStatus.NotListed)
                },
                TestIp,
                source.Token));
    }

    // ---------------------------------------------------
    // Bestehende Timeout- und Parallelitätstests
    // ---------------------------------------------------

    private static async Task CheckIgnoredCancellationTimeoutAsync()
    {
        var runner = new BlacklistBatchRunner(
            TimeSpan.FromMilliseconds(200));

        var pending = new TaskCompletionSource<BlacklistCheckResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var results = await runner.RunAsync(
                    new[]
                    {
                        new BlacklistCheckJob(
                            "psbl", (_, _) => pending.Task)
                    },
                    TestIp)
                .WaitAsync(TimeSpan.FromSeconds(5));

            var result = results["psbl"];

            Assert(result.Status == BlacklistCheckStatus.Failed,
                "Timeout wurde nicht erkannt.");

            Assert(!result.IsConclusive && result.IsListed is null,
                "Timeout wurde als belastbarer Befund interpretiert.");

            Assert(result.ErrorMessage?.Contains(
                    "Zeitlimit",
                    StringComparison.OrdinalIgnoreCase) == true,
                "Timeout-Meldung fehlt.");
        }
        finally
        {
            pending.TrySetResult(Result(
                "psbl", TestIp, BlacklistCheckStatus.NotListed));
        }
    }

    private static async Task CheckMaximumConcurrencyAsync()
    {
        var runner = new BlacklistBatchRunner(TimeSpan.FromSeconds(2));

        var pending = new TaskCompletionSource<BlacklistCheckResult>(
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
                                Interlocked.Increment(ref startedCount);
                                return pending.Task;
                            })
                    },
                    TestIp))
                .ToArray();

            var results = await Task.WhenAll(runs)
                .WaitAsync(TimeSpan.FromSeconds(8));

            Assert(Volatile.Read(ref startedCount) == 4,
                "Mehr als vier Prüfer wurden gestartet.");

            Assert(results.All(item =>
                    item["psbl"].Status == BlacklistCheckStatus.Failed),
                "Blockierte Prüfer wurden falsch bewertet.");

            Assert(results.Count(item =>
                    item["psbl"].ErrorMessage?.Contains(
                        "Kein freier Prüfplatz",
                        StringComparison.OrdinalIgnoreCase) == true) == 1,
                "Fehlender Prüfplatz wurde nicht erkannt.");
        }
        finally
        {
            pending.TrySetResult(Result(
                "psbl", TestIp, BlacklistCheckStatus.NotListed));
        }
    }

    private static async Task CheckCancellationDuringPendingCheckAsync()
    {
        var runner = new BlacklistBatchRunner(TimeSpan.FromSeconds(3));

        using var source = new CancellationTokenSource();

        var pending = new TaskCompletionSource<BlacklistCheckResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var running = runner.RunAsync(
                new[]
                {
                    new BlacklistCheckJob(
                        "psbl",
                        (_, _) =>
                        {
                            started.TrySetResult();
                            return pending.Task;
                        })
                },
                TestIp,
                source.Token);

            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            source.Cancel();

            await ExpectAsync<OperationCanceledException>(
                () => running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            pending.TrySetResult(Result(
                "psbl", TestIp, BlacklistCheckStatus.NotListed));
        }
    }

    // ---------------------------------------------------
    // NEU: Gemeinsame Tests der vier DNSBL-Profile
    // ---------------------------------------------------

    private static Task CheckProviderDefinitionsAsync()
    {
        var profiles = DnsBlacklistProviderProfiles.FirstBatch;

        Assert(profiles.Count == 4,
            "Es werden genau vier Anbieterprofile erwartet.");

        Assert(profiles.Select(p => p.BlacklistId).Distinct().Count() == 4,
            "Die Anbieterkennungen sind nicht eindeutig.");

        var knownIds = BlacklistCatalog.Entries
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expectation in ExpectedProfiles)
        {
            var profile = profiles.SingleOrDefault(
                p => p.BlacklistId == expectation.Id);

            Assert(profile is not null,
                $"Anbieterprofil fehlt: {expectation.Id}");

            Assert(knownIds.Contains(profile.BlacklistId),
                $"Katalogeintrag fehlt: {expectation.Id}");

            Assert(profile.DnsZone == expectation.Zone,
                $"DNS-Zone ist falsch: {expectation.Id}");

            Assert(profile.PositiveControlAddress == "127.0.0.2",
                $"Positive Kontrolladresse ist falsch: {expectation.Id}");

            Assert(profile.NegativeControlAddress == "127.0.0.1",
                $"Negative Kontrolladresse ist falsch: {expectation.Id}");

            Assert(profile.ListedReturnCodes.Contains(
                    expectation.PositiveCode),
                $"Positiver Kontrollcode fehlt: {expectation.Id}");

            Assert(!profile.ListedReturnCodes.Intersect(
                    profile.AccessDeniedReturnCodes).Any(),
                $"Listung und Zugriffssperre überschneiden sich: {expectation.Id}");

            // Prüft die strukturelle Gültigkeit
            // der Zone, Kontrolladressen und Rückgabecodes.
            _ = new DnsBlacklistChecker(
                profile,
                new FakeResolver(
                    new Dictionary<string, DnsBlacklistDnsResponse>()));
        }

        return Task.CompletedTask;
    }

    private static async Task CheckProviderScenariosAsync(
        ProfileTestMode mode)
    {
        foreach (var expectation in ExpectedProfiles)
        {
            var profile = DnsBlacklistProviderProfiles.FirstBatch
                .Single(p => p.BlacklistId == expectation.Id);

            var positiveQuery =
                $"2.0.0.127.{profile.DnsZone}";

            var negativeQuery =
                $"1.0.0.127.{profile.DnsZone}";

            var targetQuery =
                $"10.2.0.192.{profile.DnsZone}";

            var answers = new Dictionary<
                string, DnsBlacklistDnsResponse>();

            answers.Add(
                positiveQuery,
                mode == ProfileTestMode.FailedControl
                    ? NxDomain
                    : Addresses(expectation.PositiveCode));

            if (mode != ProfileTestMode.FailedControl)
            {
                answers.Add(negativeQuery, NxDomain);

                answers.Add(
                    targetQuery,
                    mode switch
                    {
                        ProfileTestMode.NotListed => NxDomain,
                        ProfileTestMode.Listed =>
                            Addresses(expectation.PositiveCode),
                        ProfileTestMode.Unknown =>
                            Addresses("127.0.0.254"),
                        _ => throw new InvalidOperationException(
                            "Unbekannter Testmodus.")
                    });
            }

            var resolver = new FakeResolver(answers);
            var checker = new DnsBlacklistChecker(
                profile, resolver);

            var result = await checker.CheckAsync(TestIp);

            var expectedStatus = mode switch
            {
                ProfileTestMode.NotListed =>
                    BlacklistCheckStatus.NotListed,
                ProfileTestMode.Listed =>
                    BlacklistCheckStatus.Listed,
                ProfileTestMode.Unknown =>
                    BlacklistCheckStatus.Unknown,
                ProfileTestMode.FailedControl =>
                    BlacklistCheckStatus.Failed,
                _ => throw new InvalidOperationException()
            };

            Assert(result.Status == expectedStatus,
                $"{expectation.Id}: Erwartet {expectedStatus}, " +
                $"erhalten {result.Status}.");

            Assert(result.BlacklistId == expectation.Id,
                $"{expectation.Id}: Blacklist-Kennung falsch.");

            Assert(result.CheckedTarget == TestIp,
                $"{expectation.Id}: Prüfziel falsch.");

            Assert(result.IsConclusive ==
                (mode is ProfileTestMode.NotListed
                    or ProfileTestMode.Listed),
                $"{expectation.Id}: IsConclusive falsch.");

            Assert(result.IsListed == (mode switch
            {
                ProfileTestMode.NotListed => false,
                ProfileTestMode.Listed => true,
                _ => (bool?)null
            }), $"{expectation.Id}: IsListed falsch.");

            var expectedQueries = mode == ProfileTestMode.FailedControl
                ? new[] { positiveQuery }
                : new[] { positiveQuery, negativeQuery, targetQuery };

            Assert(resolver.Queries.SequenceEqual(expectedQueries),
                $"{expectation.Id}: DNS-Abfragen falsch.");

            Assert(result.StartedAtUtc.HasValue &&
                result.CompletedAtUtc.HasValue,
                $"{expectation.Id}: Zeitstempel fehlen.");
        }
    }

    // ---------------------------------------------------
    // Gemeinsame Hilfsfunktionen
    // ---------------------------------------------------

    private static BlacklistCheckJob Job(
        string id,
        BlacklistCheckStatus status,
        string? actualId = null,
        string? actualTarget = null,
        bool withTimestamps = true)
    {
        return new BlacklistCheckJob(
            id,
            (target, token) =>
            {
                token.ThrowIfCancellationRequested();

                var now = DateTimeOffset.UtcNow;

                return Task.FromResult(new BlacklistCheckResult
                {
                    BlacklistId = actualId ?? id,
                    CheckedTarget = actualTarget ?? target,
                    Status = status,
                    StartedAtUtc = withTimestamps ? now : null,
                    CompletedAtUtc = withTimestamps ? now : null
                });
            });
    }

    private static BlacklistCheckResult Result(
        string id, string target, BlacklistCheckStatus status)
    {
        var now = DateTimeOffset.UtcNow;

        return new BlacklistCheckResult
        {
            BlacklistId = id,
            CheckedTarget = target,
            Status = status,
            StartedAtUtc = now,
            CompletedAtUtc = now
        };
    }

    private static DnsBlacklistProfile CreateTestProfile()
    {
        return new DnsBlacklistProfile(
            BlacklistId: "test-dnsbl",
            DnsZone: "test.example",
            ListedReturnCodes: new[] { "127.0.0.2" },
            AccessDeniedReturnCodes: new[] { "127.255.255.254" },
            PositiveControlAddress: "127.0.0.2",
            NegativeControlAddress: "127.0.0.1");
    }

    private static DnsBlacklistDnsResponse Addresses(
        params string[] values)
    {
        return new DnsBlacklistDnsResponse(
            false,
            values.Select(IPAddress.Parse).ToArray());
    }

    private static async Task ExpectAsync<TException>(
        Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Erwartete Ausnahme {typeof(TException).Name} fehlt.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed record Scenario(
        string Name,
        DnsBlacklistDnsResponse? Positive,
        DnsBlacklistDnsResponse? Negative,
        DnsBlacklistDnsResponse? Target,
        BlacklistCheckStatus Status,
        bool? IsListed,
        int QueryCount);

    private sealed record ProfileExpectation(
        string Id,
        string Zone,
        string PositiveCode);

    private enum ProfileTestMode
    {
        NotListed,
        Listed,
        Unknown,
        FailedControl
    }

    private sealed class FakeResolver : IDnsBlacklistResolver
    {
        private readonly
            IReadOnlyDictionary<string, DnsBlacklistDnsResponse> _answers;

        public List<string> Queries { get; } = new();

        public FakeResolver(
            IReadOnlyDictionary<string, DnsBlacklistDnsResponse> answers)
        {
            _answers = answers;
        }

        public Task<DnsBlacklistDnsResponse> QueryAsync(
            string name,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Queries.Add(name);

            if (!_answers.TryGetValue(name, out var answer))
            {
                throw new InvalidOperationException(
                    $"Simulierte DNS-Antwort fehlt: {name}");
            }

            return Task.FromResult(answer);
        }
    }
}
