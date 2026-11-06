
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

    private const string TestIpAddress =
        "192.0.2.10";

    private static readonly DnsBlacklistDnsResponse NxDomain =
        new(true, Array.Empty<IPAddress>());

    private static async Task<int> Main()
    {
        Console.WriteLine(
            "Telenec DNSBL – Regressionstests");

        Console.WriteLine();

        var passed = 0;
        var failed = 0;

        // Bestehende Tests der DNSBL-Engine.

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

        // Neue Tests des Batch-Runners.

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
            $"Ergebnis: {passed} erfolgreich, " +
            $"{failed} fehlgeschlagen.");

        return failed == 0 ? 0 : 1;

        async Task RunTestAsync(
            string name,
            Func<Task> test)
        {
            try
            {
                await test();

                Console.WriteLine(
                    $"[OK] {name}");

                passed++;
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[FEHLER] {name}");

                Console.WriteLine(
                    $"         {exception.Message}");

                failed++;
            }
        }
    }

    // ===================================================
    // Bestehende Tests der generischen DNSBL-Engine
    // ===================================================

    private static async Task RunScenarioAsync(
        Scenario scenario)
    {
        var answers =
            new Dictionary<string, DnsBlacklistDnsResponse>(
                StringComparer.OrdinalIgnoreCase);

        if (scenario.PositiveResponse is not null)
        {
            answers.Add(
                PositiveQuery,
                scenario.PositiveResponse);
        }

        if (scenario.NegativeResponse is not null)
        {
            answers.Add(
                NegativeQuery,
                scenario.NegativeResponse);
        }

        if (scenario.TargetResponse is not null)
        {
            answers.Add(
                TargetQuery,
                scenario.TargetResponse);
        }

        var resolver =
            new FakeDnsResolver(answers);

        var checker =
            new DnsBlacklistChecker(
                CreateProfile(),
                resolver);

        var result =
            await checker.CheckAsync(
                TestIpAddress);

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
            resolver.Queries.Count ==
                scenario.ExpectedQueryCount,
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
        var resolver =
            new FakeDnsResolver(
                new Dictionary<string, DnsBlacklistDnsResponse>());

        var checker =
            new DnsBlacklistChecker(
                CreateProfile(),
                resolver);

        var result =
            await checker.CheckAsync(
                "keine-ip-adresse");

        Assert(
            result.Status == BlacklistCheckStatus.Failed,
            "Ein ungültiges Prüfziel wurde nicht als Fehler bewertet.");

        Assert(
            !result.IsConclusive
            && result.IsListed is null,
            "Ein ungültiges Prüfziel wurde als verlässliches Ergebnis bewertet.");

        Assert(
            resolver.Queries.Count == 0,
            "Bei ungültigem Prüfziel wurde trotzdem DNS abgefragt.");
    }

    // ===================================================
    // Neue Tests des Blacklist-Batch-Runners
    // ===================================================

    /// <summary>
    /// Drei simulierte Anbieter werden gleichzeitig
    /// über denselben Batch-Runner geprüft.
    ///
    /// Die Ergebnisse müssen getrennt und
    /// korrekt zugeordnet zurückkommen.
    /// </summary>
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
            await runner.RunAsync(
                jobs,
                TestIpAddress);

        Assert(
            results.Count == 3,
            "Es wurden nicht alle drei Ergebnisse geliefert.");

        Assert(
            results["psbl"].Status ==
                BlacklistCheckStatus.NotListed,
            "Das PSBL-Ergebnis ist falsch.");

        Assert(
            results["psbl"].IsListed == false,
            "PSBL wurde nicht als negativer Befund bewertet.");

        Assert(
            results["spamcop"].Status ==
                BlacklistCheckStatus.Listed,
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

    /// <summary>
    /// Ein Anbieter wirft eine Exception.
    ///
    /// Der andere Anbieter muss trotzdem
    /// ein korrektes Ergebnis liefern.
    /// </summary>
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
            await runner.RunAsync(
                jobs,
                TestIpAddress);

        Assert(
            results.Count == 2,
            "Der Batch hat nicht beide Ergebnisse geliefert.");

        Assert(
            results["psbl"].Status ==
                BlacklistCheckStatus.NotListed,
            "Der funktionierende Anbieter wurde beeinträchtigt.");

        Assert(
            results["spamcop"].Status ==
                BlacklistCheckStatus.Failed,
            "Der Anbieterausfall wurde nicht als Fehler erfasst.");

        Assert(
            !results["spamcop"].IsConclusive
            && results["spamcop"].IsListed is null,
            "Der Anbieterausfall wurde als verlässlicher Befund bewertet.");
    }

    /// <summary>
    /// Ein Anbieter liefert null statt eines
    /// Prüfergebnisses.
    /// </summary>
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
            await runner.RunAsync(
                jobs,
                TestIpAddress);

        Assert(
            results["psbl"].Status ==
                BlacklistCheckStatus.Failed,
            "Ein leeres Ergebnis wurde nicht abgewiesen.");
    }

    /// <summary>
    /// Ein Prüfer meldet eine andere Kennung
    /// als die registrierte Blacklist.
    /// </summary>
    private static async Task CheckWrongBlacklistIdAsync()
    {
        var runner = new BlacklistBatchRunner();

        var results =
            await runner.RunAsync(
                new[]
                {
                    CreateJob(
                        "psbl",
                        BlacklistCheckStatus.NotListed,
                        actualId: "spamcop")
                },
                TestIpAddress);

        Assert(
            results["psbl"].Status ==
                BlacklistCheckStatus.Failed,
            "Eine falsche Blacklist-Kennung wurde akzeptiert.");
    }

    /// <summary>
    /// Der Anbieter behauptet, eine andere IP
    /// geprüft zu haben.
    /// </summary>
    private static async Task CheckWrongTargetAsync()
    {
        var runner = new BlacklistBatchRunner();

        var results =
            await runner.RunAsync(
                new[]
                {
                    CreateJob(
                        "psbl",
                        BlacklistCheckStatus.NotListed,
                        actualTarget: "192.0.2.99")
                },
                TestIpAddress);

        Assert(
            results["psbl"].Status ==
                BlacklistCheckStatus.Failed,
            "Ein abweichendes Prüfziel wurde akzeptiert.");
    }

    /// <summary>
    /// Ein bestätigtes Ergebnis ohne Zeitstempel
    /// darf nicht als gültig dargestellt werden.
    /// </summary>
    private static async Task CheckMissingTimestampsAsync()
    {
        var runner = new BlacklistBatchRunner();

        var results =
            await runner.RunAsync(
                new[]
                {
                    CreateJob(
                        "psbl",
                        BlacklistCheckStatus.NotListed,
                        includeTimestamps: false)
                },
                TestIpAddress);

        Assert(
            results["psbl"].Status ==
                BlacklistCheckStatus.Failed,
            "Ein Prüfergebnis ohne Zeitstempel wurde akzeptiert.");

        Assert(
            results["psbl"].IsListed is null,
            "Ein fehlerhaftes Prüfergebnis wurde als negativ bewertet.");
    }

    /// <summary>
    /// Dieselbe Blacklist darf nicht mehrfach
    /// in einem Batch registriert werden.
    /// </summary>
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

    /// <summary>
    /// Unbekannte Katalogkennungen werden
    /// vor dem Start des Batches abgewiesen.
    /// </summary>
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

    /// <summary>
    /// Der Batch akzeptiert ausschließlich
    /// gültige IPv4-Prüfziele.
    /// </summary>
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

    /// <summary>
    /// Ein ausdrücklicher Abbruch durch den
    /// aufrufenden Dienst darf nicht als
    /// gewöhnlicher Anbieterfehler verschwinden.
    /// </summary>
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
    // Hilfsfunktionen
    // ===================================================

    /// <summary>
    /// Erzeugt einen simulierten Anbieter.
    ///
    /// Es erfolgt ausdrücklich keine
    /// echte DNS- oder API-Abfrage.
    /// </summary>
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

    /// <summary>
    /// Simulierter DNS-Resolver für die
    /// bestehenden DNSBL-Regressionstests.
    /// </summary>
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
