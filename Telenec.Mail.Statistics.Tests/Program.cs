
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

    private static readonly DnsBlacklistDnsResponse NxDomain =
        new(true, Array.Empty<IPAddress>());

    private static async Task<int> Main()
    {
        Console.WriteLine("Telenec DNSBL – Regressionstests");
        Console.WriteLine();

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

        var passed = 0;
        var failed = 0;

        foreach (var scenario in scenarios)
        {
            try
            {
                await RunScenarioAsync(scenario);

                Console.WriteLine(
                    $"[OK] {scenario.Name}");

                passed++;
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[FEHLER] {scenario.Name}");

                Console.WriteLine(
                    $"         {exception.Message}");

                failed++;
            }
        }

        try
        {
            await CheckInvalidAddressAsync();

            Console.WriteLine(
                "[OK] Ungültiges Prüfziel");

            passed++;
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                "[FEHLER] Ungültiges Prüfziel");

            Console.WriteLine(
                $"         {exception.Message}");

            failed++;
        }

        Console.WriteLine();

        Console.WriteLine(
            $"Ergebnis: {passed} erfolgreich, {failed} fehlgeschlagen.");

        return failed == 0 ? 0 : 1;
    }

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

        var resolver = new FakeDnsResolver(answers);

        var checker =
            new DnsBlacklistChecker(
                CreateProfile(),
                resolver);

        var result =
            await checker.CheckAsync(
                "192.0.2.10");

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
            result.CheckedTarget == "192.0.2.10",
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

    private static DnsBlacklistProfile CreateProfile()
    {
        return new DnsBlacklistProfile(
            BlacklistId: "test-dnsbl",
            DnsZone: "test.example",
            ListedReturnCodes: new[] { "127.0.0.2" },
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
