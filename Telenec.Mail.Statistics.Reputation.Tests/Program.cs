
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Telenec.Mail.Statistics.Server.Reputation;

namespace Telenec.Mail.Statistics.Reputation.Tests;

internal static class Program
{
    private const string TestIp = "192.0.2.10";

    private static readonly string[] MailspikePositiveCodes =
    {
        "127.0.0.2",
        "127.0.0.10",
        "127.0.0.11",
        "127.0.0.12",
        "127.0.0.13"
    };

    private static readonly DnsBlacklistDnsResponse NxDomain =
        new(true, Array.Empty<IPAddress>());

    private static int _passed;
    private static int _failed;

    private static async Task<int> Main()
    {
        Console.WriteLine(
            "Telenec DNSBL – Erweiterungs-Regressionstests");
        Console.WriteLine();

        await Test("Erweiterte Anbieterprofile initialisieren", () =>
        {
            var publicProfiles =
                ExtendedDnsBlacklistProviderProfiles
                    .PublicDnsblProfiles;

            var reviewProfiles =
                ExtendedDnsBlacklistProviderProfiles
                    .ReviewDnsblProfiles;

            var deferredProfiles =
                ExtendedDnsBlacklistProviderProfiles
                    .DeferredDnsblProfiles;

            Assert(publicProfiles.Count == 13,
                "Es werden 13 Erweiterungsprofile erwartet.");

            Assert(reviewProfiles.Count == 5,
                "Es werden fünf Prüfprofile erwartet.");

            Assert(deferredProfiles.Count == 2,
                "Es werden zwei zurückgestellte Profile erwartet.");

            var allProfiles = publicProfiles
                .Concat(reviewProfiles)
                .Concat(deferredProfiles)
                .ToArray();

            Assert(
                allProfiles.Select(p => p.BlacklistId)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == allProfiles.Length,
                "Doppelte Anbieterkennungen gefunden.");

            foreach (var profile in allProfiles)
            {
                Assert(profile.AccessDeniedReturnCodes is not null,
                    $"Zugriffssperrcodes fehlen: {profile.BlacklistId}");

                _ = new DnsBlacklistChecker(profile);
            }

            return Task.CompletedTask;
        });

        await Test("Mailspike: Sondercode nur für Kontrolle", () =>
        {
            var profile = MailspikeProfile();

            Assert(
                profile.PositiveControlAdditionalReturnCodes?
                    .Contains("127.0.0.13") == true,
                "127.0.0.13 muss als Kontrollcode bekannt sein.");

            Assert(
                !profile.ListedReturnCodes.Contains("127.0.0.13"),
                "127.0.0.13 darf kein Listungscode sein.");

            return Task.CompletedTask;
        });

        await Test("Mailspike: Negative Zielprüfung", async () =>
        {
            var result = await CheckMailspikeAsync(
                MailspikePositiveCodes,
                NxDomain,
                NxDomain);

            Assert(result.Status == BlacklistCheckStatus.NotListed,
                "Eine NXDOMAIN-Zielantwort muss NotListed ergeben.");

            Assert(result.IsListed == false,
                "Die Negativbewertung ist inkonsistent.");
        });

        await Test("Mailspike: Eindeutige Listung", async () =>
        {
            var result = await CheckMailspikeAsync(
                MailspikePositiveCodes,
                NxDomain,
                Addresses("127.0.0.12"));

            Assert(result.Status == BlacklistCheckStatus.Listed,
                "Ein echter Listungscode muss Listed ergeben.");

            Assert(result.IsListed == true,
                "Die Positivbewertung ist inkonsistent.");
        });

        await Test("Mailspike: L2 ist keine Listung", async () =>
        {
            var result = await CheckMailspikeAsync(
                MailspikePositiveCodes,
                NxDomain,
                Addresses("127.0.0.13"));

            Assert(result.Status == BlacklistCheckStatus.Unknown,
                "L2 darf keine bestätigte Listung ergeben.");

            Assert(result.IsListed is null,
                "L2 darf nicht in einen Bool-Wert umgewandelt werden.");
        });

        await Test("Mailspike: Gemischte Zielantwort", async () =>
        {
            var result = await CheckMailspikeAsync(
                MailspikePositiveCodes,
                NxDomain,
                Addresses("127.0.0.12", "127.0.0.13"));

            Assert(result.Status == BlacklistCheckStatus.Unknown,
                "Gemischte Reputationsstufen müssen unklar bleiben.");
        });

        await Test("Mailspike: Nur L2 als Positivkontrolle", async () =>
        {
            var result = await CheckMailspikeAsync(
                new[] { "127.0.0.13" },
                NxDomain,
                NxDomain);

            Assert(result.Status == BlacklistCheckStatus.Failed,
                "Ohne echten Listungscode muss die Kontrolle scheitern.");

            Assert(
                result.ErrorMessage?.Contains(
                    "positive Kontrollabfrage",
                    StringComparison.OrdinalIgnoreCase) == true,
                "Die Fehlerursache muss die Positivkontrolle sein.");
        });

        await Test("Mailspike: Unbekannter Kontrollcode", async () =>
        {
            var result = await CheckMailspikeAsync(
                MailspikePositiveCodes
                    .Append("127.0.0.99")
                    .ToArray(),
                NxDomain,
                NxDomain);

            Assert(result.Status == BlacklistCheckStatus.Failed,
                "Unbekannte Kontrollcodes dürfen nicht akzeptiert werden.");

            Assert(
                result.ErrorMessage?.Contains(
                    "positive Kontrollabfrage",
                    StringComparison.OrdinalIgnoreCase) == true,
                "Die Positivkontrolle muss den Fehler melden.");
        });

        await Test("Mailspike: Defekte Negativkontrolle", async () =>
        {
            var result = await CheckMailspikeAsync(
                MailspikePositiveCodes,
                Addresses("127.0.0.2"),
                NxDomain);

            Assert(result.Status == BlacklistCheckStatus.Failed,
                "Eine Negativkontrolle ohne NXDOMAIN muss scheitern.");
        });

        await Test("Mailspike: Zugriff verweigert", async () =>
        {
            var result = await CheckMailspikeAsync(
                MailspikePositiveCodes,
                NxDomain,
                Addresses("127.255.255.255"));

            Assert(
                result.Status == BlacklistCheckStatus.AccessDenied,
                "Ein Zugriffssperrcode darf keine Listung ergeben.");

            Assert(result.IsListed is null,
                "Ein verweigerter Zugriff ist kein Listungsstatus.");
        });

        await Test("Bisherige Profile bleiben unverändert", async () =>
        {
            var profile = DnsBlacklistProviderProfiles
                .FirstBatch
                .Single(p => p.BlacklistId == "0spam");

            var resolver = new FixedResolver(
                new Dictionary<string, DnsBlacklistDnsResponse>
                {
                    [QueryName(
                        profile.PositiveControlAddress,
                        profile.DnsZone)] =
                        Addresses("127.0.0.2", "127.0.0.13")
                });

            var result = await new DnsBlacklistChecker(
                profile,
                resolver).CheckAsync(TestIp);

            Assert(result.Status == BlacklistCheckStatus.Failed,
                "Alte Profile dürfen zusätzliche Codes nicht erlauben.");

            Assert(
                result.ErrorMessage?.Contains(
                    "positive Kontrollabfrage",
                    StringComparison.OrdinalIgnoreCase) == true,
                "Die bestehende Positivkontrolle wurde verändert.");
        });

        await Test("Standardkonfiguration: acht Anbieter", () =>
        {
            var snapshot = CreateSnapshot(
                extended: false,
                review: false);

            Assert(snapshot.Results.Count == 8,
                "Die Standardkonfiguration muss acht Prüfer enthalten.");

            Assert(snapshot.Results.ContainsKey("psbl"),
                "PSBL fehlt.");

            return Task.CompletedTask;
        });

        await Test("Erweiterung: genau 21 Anbieter", () =>
        {
            var snapshot = CreateSnapshot(
                extended: true,
                review: false);

            Assert(snapshot.Results.Count == 21,
                "Die Erweiterung muss genau 21 Prüfer enthalten.");

            foreach (var profile in
                ExtendedDnsBlacklistProviderProfiles.PublicDnsblProfiles)
            {
                Assert(snapshot.Results.ContainsKey(profile.BlacklistId),
                    $"Aktiver Anbieter fehlt: {profile.BlacklistId}");
            }

            Assert(!snapshot.Results.ContainsKey("hostkarma-black"),
                "HostKarma darf nicht automatisch aktiv sein.");

            Assert(!snapshot.Results.ContainsKey("imp-spam"),
                "IMP SPAM darf nicht automatisch aktiv sein.");

            Assert(snapshot.Results.Values.All(
                r => r.Status == BlacklistCheckStatus.NotChecked
                  && r.IsListed is null),
                "Vor dem ersten Scan müssen alle Prüfer ungeprüft sein.");

            var overview = BlacklistOverviewFactory.Create(
                snapshot.Results);

            Assert(
                overview.TotalCount == 68 &&
                overview.IntegratedCount == 21 &&
                overview.NotIntegratedCount == 47,
                "Die Katalogübersicht ist inkonsistent.");

            return Task.CompletedTask;
        });

        await Test("Review-Modus: fünf weitere Anbieter", () =>
        {
            var snapshot = CreateSnapshot(
                extended: true,
                review: true);

            Assert(snapshot.Results.Count == 26,
                "Mit Review-Modus werden 26 Prüfer erwartet.");

            foreach (var profile in
                ExtendedDnsBlacklistProviderProfiles.ReviewDnsblProfiles)
            {
                Assert(snapshot.Results.ContainsKey(profile.BlacklistId),
                    $"Review-Anbieter fehlt: {profile.BlacklistId}");
            }

            return Task.CompletedTask;
        });

        Console.WriteLine();
        Console.WriteLine(
            $"Ergebnis: {_passed} erfolgreich, {_failed} fehlgeschlagen.");

        return _failed == 0 ? 0 : 1;
    }

    private static async Task Test(
        string name,
        Func<Task> action)
    {
        try
        {
            await action();
            _passed++;
            Console.WriteLine($"[OK] {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine($"[FEHLER] {name}");
            Console.WriteLine($"  {exception.Message}");
        }
    }

    private static DnsBlacklistProfile MailspikeProfile()
    {
        return ExtendedDnsBlacklistProviderProfiles
            .PublicDnsblProfiles
            .Single(p => p.BlacklistId == "mailspike-bl");
    }

    private static async Task<BlacklistCheckResult>
        CheckMailspikeAsync(
            string[] positiveCodes,
            DnsBlacklistDnsResponse negativeResponse,
            DnsBlacklistDnsResponse targetResponse)
    {
        var profile = MailspikeProfile();

        var responses =
            new Dictionary<string, DnsBlacklistDnsResponse>(
                StringComparer.OrdinalIgnoreCase)
            {
                [QueryName(
                    profile.PositiveControlAddress,
                    profile.DnsZone)] =
                    Addresses(positiveCodes),

                [QueryName(
                    profile.NegativeControlAddress,
                    profile.DnsZone)] =
                    negativeResponse,

                [QueryName(TestIp, profile.DnsZone)] =
                    targetResponse
            };

        var checker = new DnsBlacklistChecker(
            profile,
            new FixedResolver(responses));

        return await checker.CheckAsync(TestIp);
    }

    private static BlacklistMonitoringSnapshot CreateSnapshot(
        bool extended,
        bool review)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Reputation:Hostname"] = "test.example",
                    ["Reputation:ExpectedIpv4"] = TestIp,
                    ["Reputation:EnableExtendedDnsbl"] =
                        extended.ToString(),
                    ["Reputation:EnableReviewDnsbl"] =
                        review.ToString()
                })
            .Build();

        using var service = new BlacklistMonitoringService(
            configuration,
            TimeProvider.System,
            NullLogger<BlacklistMonitoringService>.Instance);

        return service.GetSnapshot();
    }

    private static string QueryName(
        string ipv4,
        string dnsZone)
    {
        var bytes = IPAddress.Parse(ipv4).GetAddressBytes();

        return string.Join(".", bytes.Reverse()) +
            "." + dnsZone;
    }

    private static DnsBlacklistDnsResponse Addresses(
        params string[] values)
    {
        return new DnsBlacklistDnsResponse(
            false,
            values.Select(IPAddress.Parse).ToArray());
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

    private sealed class FixedResolver : IDnsBlacklistResolver
    {
        private readonly IReadOnlyDictionary<
            string, DnsBlacklistDnsResponse> _responses;

        public FixedResolver(
            IReadOnlyDictionary<string, DnsBlacklistDnsResponse> responses)
        {
            _responses = responses;
        }

        public Task<DnsBlacklistDnsResponse> QueryAsync(
            string queryName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_responses.TryGetValue(
                queryName,
                out var response))
            {
                throw new InvalidOperationException(
                    $"Nicht simulierte DNS-Abfrage: {queryName}");
            }

            return Task.FromResult(response);
        }
    }
}
