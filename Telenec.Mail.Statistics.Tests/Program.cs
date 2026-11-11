
using System.Diagnostics.CodeAnalysis;
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

    private static readonly ProfileExpectation[] FirstProfiles =
    {
        new("0spam", "bl.0spam.org", "127.0.0.2"),
        new("0spam-rbl", "rbl.0spam.org", "127.0.0.2"),
        new("drone-bl", "dnsbl.dronebl.org", "127.0.0.1"),
        new("uceprotectl1", "dnsbl-1.uceprotect.net", "127.0.0.2")
    };

    private static readonly ProfileExpectation[] SecondProfiles =
    {
        new("blocklist-de", "bl.blocklist.de", "127.0.0.2"),
        new("spamcop", "bl.spamcop.net", "127.0.0.2"),
        new("uceprotectl2", "dnsbl-2.uceprotect.net", "127.0.0.2"),
        new("uceprotectl3", "dnsbl-3.uceprotect.net", "127.0.0.2")
    };

    private static readonly string[] ActiveBlacklistIds =
    {
        "psbl",
        "0spam",
        "0spam-rbl",
        "uceprotectl1",
        "blocklist-de",
        "uceprotectl2",
        "uceprotectl3"
    };

    /*
     * Verbindlicher und unabhängiger Referenzbestand.
     *
     * Alle 68 Kennungen werden einschließlich
     * ihrer bisherigen Reihenfolge abgesichert.
     */
    private static readonly string[] ExpectedReferenceIds =
    {
        "ivmuri",
        "sem-fresh",
        "sem-uri",
        "sem-urired",
        "sorbs-rhsbl-badconf",
        "sorbs-rhsbl-nomail",
        "spamhaus-dbl",
        "surbl-multi",
        "0spam",
        "0spam-rbl",
        "abusix-mail-intelligence-blacklist",
        "abusix-mail-intelligence-domain-blacklist",
        "abusix-mail-intelligence-exploit-list",
        "anonmails-dnsbl",
        "backscatterer",
        "barracuda",
        "blocklist-de",
        "calivent",
        "cymru-bogons",
        "dan-tor",
        "dan-torexit",
        "drmx",
        "drone-bl",
        "fabelsources",
        "hil",
        "hil2",
        "hostkarma-black",
        "ibm-dns-blacklist",
        "icmforbidden",
        "imp-spam",
        "imp-worm",
        "interserver",
        "ivmsip",
        "ivmsip24",
        "jippg",
        "kemptbl",
        "konstant",
        "lashback",
        "mailspike-bl",
        "mailspike-z",
        "msrbl-phishing",
        "msrbl-spam",
        "netherrelays",
        "netherunsure",
        "nosolicitado",
        "psbl",
        "rats-dyna",
        "rats-noptr",
        "rats-spam",
        "rbl-jp",
        "s5h-net",
        "schulte",
        "sem-backscatter",
        "sem-black",
        "sender-score-reputation-network",
        "servicesnet",
        "spamcop",
        "spamhaus-zen",
        "suomispam-reputation",
        "swinog",
        "triumf",
        "truncate",
        "uceprotectl1",
        "uceprotectl2",
        "uceprotectl3",
        "woodys-smtp-blacklist",
        "zapbl",
        "spfbl-dnsbl"
    };

    // ================================================
    // Sprint 30.2
    // ================================================

    private static readonly CatalogMetadataExpectation[]
        Sprint302Expectations =
        {
            new(
                "backscatterer",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "barracuda",
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistReputationScope.SingleIp),

            new(
                "cymru-bogons",
                BlacklistAccessStatus.Permitted,
                BlacklistReputationScope.IpNetwork),

            new(
                "dan-tor",
                BlacklistAccessStatus.Permitted,
                BlacklistReputationScope.SingleIp),

            new(
                "dan-torexit",
                BlacklistAccessStatus.Permitted,
                BlacklistReputationScope.SingleIp),

            new(
                "rats-dyna",
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistReputationScope.SingleIp),

            new(
                "rats-noptr",
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistReputationScope.SingleIp),

            new(
                "rats-spam",
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistReputationScope.SingleIp)
        };

    // ================================================
    // Sprint 30.3
    // ================================================

    private static readonly CatalogMetadataExpectation[]
        Sprint303Expectations =
        {
            new(
                "anonmails-dnsbl",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "hostkarma-black",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "mailspike-bl",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "mailspike-z",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "s5h-net",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "suomispam-reputation",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.Mixed),

            new(
                "spfbl-dnsbl",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.Mixed)
        };

    // ================================================
    // Sprint 30.4
    // ================================================

    private static readonly CatalogMetadataExpectation[]
        Sprint304Expectations =
        {
            new(
                "imp-spam",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "imp-worm",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "interserver",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "lashback",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "msrbl-phishing",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "msrbl-spam",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "swinog",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp)
        };

    // ================================================
    // Sprint 30.5
    // ================================================

    /*
     * Sechs weitere IPv4-DNSBL-Referenzen.
     *
     * CALIVENT, DrMX, TRUNCATE und Woody's
     * bleiben hinsichtlich der Nutzungsfreigabe
     * auf NotReviewed.
     *
     * Fabelsources und ZapBL sind als
     * Permitted klassifiziert.
     *
     * Mixed berücksichtigt potenziell
     * netzbezogene Listungseffekte.
     *
     * Diese Tests ersetzen keine Prüfung
     * der tatsächlichen DNS-Erreichbarkeit.
     */
    private static readonly CatalogMetadataExpectation[]
        Sprint305Expectations =
        {
            new(
                "calivent",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "drmx",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "fabelsources",
                BlacklistAccessStatus.Permitted,
                BlacklistReputationScope.Mixed),

            new(
                "truncate",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "woodys-smtp-blacklist",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "zapbl",
                BlacklistAccessStatus.Permitted,
                BlacklistReputationScope.Mixed)
        };

    // ================================================
    // Sprint 30.6
    // ================================================

    /*
     * Abschließende Klassifizierung der letzten
     * 15 historischen Referenzquellen.
     *
     * Alle Einträge sind als IPv4-basierte
     * DNS-Reputationsquellen dokumentiert.
     *
     * Das bedeutet weder eine bestätigte
     * aktuelle Erreichbarkeit noch eine
     * automatische Nutzungsfreigabe.
     *
     * HIL und Sender Score erfordern eine
     * gesonderte Autorisierung.
     *
     * SCHULTE ist aufgrund dokumentierter
     * öffentlicher Nutzung als Permitted
     * klassifiziert.
     *
     * Alle übrigen bleiben auf NotReviewed.
     *
     * SERVICESNET bewertet Netzbereiche.
     * SCHULTE und KEMPTBL berücksichtigen
     * auch netzbezogene Listungen.
     *
     * Diese Tests führen keine DNS-Abfragen aus
     * und aktivieren keine Anbieterprofile.
     */
    private static readonly CatalogMetadataExpectation[]
        Sprint306Expectations =
        {
            new(
                "hil",
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistReputationScope.SingleIp),

            new(
                "hil2",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "ibm-dns-blacklist",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "icmforbidden",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "jippg",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "kemptbl",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.Mixed),

            new(
                "konstant",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "netherrelays",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "netherunsure",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "nosolicitado",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "rbl-jp",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp),

            new(
                "schulte",
                BlacklistAccessStatus.Permitted,
                BlacklistReputationScope.Mixed),

            new(
                "sender-score-reputation-network",
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistReputationScope.SingleIp),

            new(
                "servicesnet",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.IpNetwork),

            new(
                "triumf",
                BlacklistAccessStatus.NotReviewed,
                BlacklistReputationScope.SingleIp)
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
            await Test(
                scenario.Name,
                () => RunDnsScenarioAsync(scenario));
        }

        await Test(
            "Ungültiges Prüfziel",
            CheckInvalidAddressAsync);

        Console.WriteLine();
        Console.WriteLine(
            "Blacklist-Batch – Mehrfachanbieter-Tests");
        Console.WriteLine();

        await Test(
            "Mehrere Anbieter in einem Durchlauf",
            CheckMultipleProvidersAsync);

        await Test(
            "Anbieterfehler bleiben isoliert",
            CheckFailureIsolationAsync);

        await Test(
            "Leeres Prüfergebnis wird abgewiesen",
            CheckNullResultAsync);

        await Test(
            "Falsche Blacklist-Kennung wird abgewiesen",
            CheckWrongIdAsync);

        await Test(
            "Abweichendes Prüfziel wird abgewiesen",
            CheckWrongTargetAsync);

        await Test(
            "Ungültige Prüfzeitpunkte werden abgewiesen",
            CheckMissingTimestampsAsync);

        await Test(
            "Doppelte Anbieterregistrierung wird abgewiesen",
            CheckDuplicateRegistrationAsync);

        await Test(
            "Unbekannter Anbieter wird abgewiesen",
            CheckUnknownProviderAsync);

        await Test(
            "Ungültige Batch-IP wird abgewiesen",
            CheckInvalidBatchAddressAsync);

        await Test(
            "Programmabbruch wird weitergegeben",
            CheckBatchCancellationAsync);

        Console.WriteLine();
        Console.WriteLine(
            "Blacklist-Batch – Timeout- und Lasttests");
        Console.WriteLine();

        await Test(
            "Ignorierter Abbruch führt zu Timeout",
            CheckIgnoredCancellationTimeoutAsync);

        await Test(
            "Maximal vier laufende Anbieter",
            CheckMaximumConcurrencyAsync);

        await Test(
            "Abbruch während hängender Prüfung",
            CheckCancellationDuringPendingCheckAsync);

        Console.WriteLine();
        Console.WriteLine(
            "DNSBL-Anbieterprofile – Erste Gruppe");
        Console.WriteLine();

        await Test(
            "Vier eindeutige, gültige Anbieterprofile",
            () => CheckDefinitionsAsync(
                DnsBlacklistProviderProfiles.FirstBatch,
                FirstProfiles));

        foreach (var mode in Enum.GetValues<ProfileTestMode>())
        {
            await Test(
                $"Vier Anbieter: {ModeName(mode)}",
                () => CheckProfilesAsync(
                    DnsBlacklistProviderProfiles.FirstBatch,
                    FirstProfiles,
                    mode));
        }

        Console.WriteLine();
        Console.WriteLine(
            "DNSBL-Anbieterprofile – Zweite Gruppe");
        Console.WriteLine();

        await Test(
            "Vier eindeutige, gültige Anbieterprofile",
            () => CheckDefinitionsAsync(
                DnsBlacklistProviderProfiles.SecondBatch,
                SecondProfiles));

        foreach (var mode in Enum.GetValues<ProfileTestMode>())
        {
            await Test(
                $"Vier Anbieter: {ModeName(mode)}",
                () => CheckProfilesAsync(
                    DnsBlacklistProviderProfiles.SecondBatch,
                    SecondProfiles,
                    mode));
        }

        await Test(
            "blocklist.de: höherer Listungscode",
            CheckBlocklistDeCodeAsync);

        Console.WriteLine();
        Console.WriteLine(
            "Blacklist-Katalog – Sprint 30");
        Console.WriteLine();

        await Test(
            "68 eindeutige Referenzeinträge",
            CheckCatalogStructureAsync);

        await Test(
            "Rückwärtskompatibler Katalog-Konstruktor",
            CheckLegacyCatalogConstructorAsync);

        await Test(
            "Metadaten der sieben aktiven Listen",
            CheckActiveCatalogMetadataAsync);

        await Test(
            "Domain- und URI-Prüfarten",
            CheckDomainAndUriMetadataAsync);

        await Test(
            "UCEPROTECT: IP-, Netz- und ASN-Bewertung",
            CheckUceProtectScopesAsync);

        await Test(
            "Eingestellte Quellen bleiben dokumentiert",
            CheckDiscontinuedSourcesAsync);

        await Test(
            "Übersicht mit sieben simulierten Ergebnissen",
            CheckCatalogOverviewAsync);

        Console.WriteLine();
        Console.WriteLine(
            "Blacklist-Katalog – Sprint 30.2");
        Console.WriteLine();

        await Test(
            "Exakte 68 Referenzkennungen und Reihenfolge",
            CheckCompleteReferenceSetAsync);

        foreach (var expectation in Sprint302Expectations)
        {
            await Test(
                $"Anbieter-Metadaten: {expectation.Id}",
                () => CheckIpProviderMetadataAsync(expectation));
        }

        await Test(
            "Neue Katalogeinträge werden nicht automatisch aktiv",
            CheckSprint302InactiveAsync);

        Console.WriteLine();
        Console.WriteLine(
            "Blacklist-Katalog – Sprint 30.3");
        Console.WriteLine();

        foreach (var expectation in Sprint303Expectations)
        {
            await Test(
                $"Anbieter-Metadaten: {expectation.Id}",
                () => CheckIpProviderMetadataAsync(expectation));
        }

        Console.WriteLine();
        Console.WriteLine(
            "Blacklist-Katalog – Sprint 30.4");
        Console.WriteLine();

        foreach (var expectation in Sprint304Expectations)
        {
            await Test(
                $"Anbieter-Metadaten: {expectation.Id}",
                () => CheckIpProviderMetadataAsync(expectation));
        }

        // ================================================
        // Sprint 30.5
        // ================================================

        Console.WriteLine();
        Console.WriteLine(
            "Blacklist-Katalog – Sprint 30.5");
        Console.WriteLine();

        foreach (var expectation in Sprint305Expectations)
        {
            await Test(
                $"Anbieter-Metadaten: {expectation.Id}",
                () => CheckIpProviderMetadataAsync(expectation));
        }

        // ================================================
        // Sprint 30.6
        // ================================================

        Console.WriteLine();
        Console.WriteLine(
            "Blacklist-Katalog – Sprint 30.6");
        Console.WriteLine();

        foreach (var expectation in Sprint306Expectations)
        {
            await Test(
                $"Anbieter-Metadaten: {expectation.Id}",
                () => CheckIpProviderMetadataAsync(expectation));
        }

        await Test(
            "Alle 68 Referenzen technisch klassifiziert",
            CheckAllSourcesClassifiedAsync);

        await Test(
            "Neue Quellen ohne Prüfergebnis bleiben offen",
            CheckSprint306NotIntegratedAsync);

        Console.WriteLine();
        Console.WriteLine(
            $"Ergebnis: {passed} erfolgreich, {failed} fehlgeschlagen.");

        return failed == 0 ? 0 : 1;
    }

    // ===================================================
    // DNSBL-Regressionstests
    // ===================================================

    private static async Task RunDnsScenarioAsync(
        Scenario scenario)
    {
        var answers =
            new Dictionary<string, DnsBlacklistDnsResponse>(
                StringComparer.OrdinalIgnoreCase);

        if (scenario.Positive is not null)
        {
            answers.Add(
                PositiveQuery,
                scenario.Positive);
        }

        if (scenario.Negative is not null)
        {
            answers.Add(
                NegativeQuery,
                scenario.Negative);
        }

        if (scenario.Target is not null)
        {
            answers.Add(
                TargetQuery,
                scenario.Target);
        }

        var resolver = new FakeResolver(answers);

        var checker = new DnsBlacklistChecker(
            CreateTestProfile(),
            resolver);

        var result = await checker.CheckAsync(TestIp);

        Assert(
            result.Status == scenario.Status,
            $"Erwartet {scenario.Status}, erhalten {result.Status}");

        Assert(
            result.IsListed == scenario.IsListed,
            "IsListed ist falsch.");

        Assert(
            result.IsConclusive ==
                (scenario.Status is
                    BlacklistCheckStatus.Listed
                    or BlacklistCheckStatus.NotListed),
            "IsConclusive ist falsch.");

        Assert(
            result.BlacklistId == "test-dnsbl",
            "Blacklist-Kennung ist falsch.");

        Assert(
            result.CheckedTarget == TestIp,
            "Prüfziel ist falsch.");

        Assert(
            result.StartedAtUtc.HasValue &&
            result.CompletedAtUtc.HasValue,
            "Zeitstempel fehlen.");

        Assert(
            resolver.Queries.Count == scenario.QueryCount,
            "Anzahl der DNS-Abfragen ist falsch.");

        Assert(
            resolver.Queries.SequenceEqual(
                new[]
                {
                    PositiveQuery,
                    NegativeQuery,
                    TargetQuery
                }.Take(scenario.QueryCount),
                StringComparer.OrdinalIgnoreCase),
            "DNS-Abfragen wurden in falscher Reihenfolge ausgeführt.");
    }

    private static async Task CheckInvalidAddressAsync()
    {
        var resolver = new FakeResolver(
            new Dictionary<string, DnsBlacklistDnsResponse>());

        var checker = new DnsBlacklistChecker(
            CreateTestProfile(),
            resolver);

        var result = await checker.CheckAsync(
            "keine-ip-adresse");

        Assert(
            result.Status == BlacklistCheckStatus.Failed,
            "Ungültige IP wurde nicht abgewiesen.");

        Assert(
            !result.IsConclusive &&
            result.IsListed is null,
            "Ungültige IP wurde als belastbarer Befund bewertet.");

        Assert(
            resolver.Queries.Count == 0,
            "Trotz ungültiger IP wurde DNS abgefragt.");
    }

    // ===================================================
    // Batch-Regressionstests
    // ===================================================

    private static async Task CheckMultipleProvidersAsync()
    {
        var results = await new BlacklistBatchRunner().RunAsync(
            new[]
            {
                Job(
                    "psbl",
                    BlacklistCheckStatus.NotListed),

                Job(
                    "spamcop",
                    BlacklistCheckStatus.Listed),

                Job(
                    "blocklist-de",
                    BlacklistCheckStatus.AccessDenied)
            },
            TestIp);

        Assert(
            results.Count == 3,
            "Es fehlen Ergebnisse.");

        Assert(
            results["psbl"].Status ==
                BlacklistCheckStatus.NotListed &&
            results["psbl"].IsListed == false,
            "PSBL ist falsch.");

        Assert(
            results["spamcop"].Status ==
                BlacklistCheckStatus.Listed &&
            results["spamcop"].IsListed == true,
            "Simulierter Listungstreffer ist falsch.");

        Assert(
            results["blocklist-de"].Status ==
                BlacklistCheckStatus.AccessDenied &&
            results["blocklist-de"].IsListed is null,
            "Zugriffssperre ist falsch.");
    }

    private static async Task CheckFailureIsolationAsync()
    {
        var results = await new BlacklistBatchRunner().RunAsync(
            new[]
            {
                Job(
                    "psbl",
                    BlacklistCheckStatus.NotListed),

                new BlacklistCheckJob(
                    "spamcop",
                    (_, _) => throw new InvalidOperationException(
                        "Simulierter Ausfall"))
            },
            TestIp);

        Assert(
            results.Count == 2,
            "Ein Ergebnis fehlt.");

        Assert(
            results["psbl"].Status ==
                BlacklistCheckStatus.NotListed,
            "Funktionierender Anbieter wurde beeinträchtigt.");

        Assert(
            results["spamcop"].Status ==
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
                    (_, _) =>
                        Task.FromResult<BlacklistCheckResult>(
                            null!))
            },
            TestIp);

        Assert(
            results["psbl"].Status ==
                BlacklistCheckStatus.Failed,
            "Null-Ergebnis wurde akzeptiert.");
    }

    private static async Task CheckWrongIdAsync()
    {
        var results = await new BlacklistBatchRunner().RunAsync(
            new[]
            {
                Job(
                    "psbl",
                    BlacklistCheckStatus.NotListed,
                    actualId: "spamcop")
            },
            TestIp);

        Assert(
            results["psbl"].Status ==
                BlacklistCheckStatus.Failed,
            "Falsche Blacklist-Kennung wurde akzeptiert.");
    }

    private static async Task CheckWrongTargetAsync()
    {
        var results = await new BlacklistBatchRunner().RunAsync(
            new[]
            {
                Job(
                    "psbl",
                    BlacklistCheckStatus.NotListed,
                    actualTarget: "192.0.2.99")
            },
            TestIp);

        Assert(
            results["psbl"].Status ==
                BlacklistCheckStatus.Failed,
            "Falsches Prüfziel wurde akzeptiert.");
    }

    private static async Task CheckMissingTimestampsAsync()
    {
        var results = await new BlacklistBatchRunner().RunAsync(
            new[]
            {
                Job(
                    "psbl",
                    BlacklistCheckStatus.NotListed,
                    withTimestamps: false)
            },
            TestIp);

        Assert(
            results["psbl"].Status ==
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
                    Job(
                        "psbl",
                        BlacklistCheckStatus.NotListed),

                    Job(
                        "psbl",
                        BlacklistCheckStatus.NotListed)
                },
                TestIp));
    }

    private static async Task CheckUnknownProviderAsync()
    {
        await ExpectAsync<InvalidOperationException>(
            () => new BlacklistBatchRunner().RunAsync(
                new[]
                {
                    Job(
                        "anbieter-existiert-nicht",
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
                    Job(
                        "psbl",
                        BlacklistCheckStatus.NotListed)
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
                    Job(
                        "psbl",
                        BlacklistCheckStatus.NotListed)
                },
                TestIp,
                source.Token));
    }

    // ===================================================
    // Timeout- und Parallelitätstests
    // ===================================================

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
                TestIp).WaitAsync(
                    TimeSpan.FromSeconds(5));

            var result = results["psbl"];

            Assert(
                result.Status == BlacklistCheckStatus.Failed,
                "Timeout wurde nicht erkannt.");

            Assert(
                !result.IsConclusive &&
                result.IsListed is null,
                "Timeout wurde als belastbarer Befund interpretiert.");

            Assert(
                result.ErrorMessage?.Contains(
                    "Zeitlimit",
                    StringComparison.OrdinalIgnoreCase) == true,
                "Timeout-Meldung fehlt.");
        }
        finally
        {
            pending.TrySetResult(
                Result(
                    "psbl",
                    TestIp,
                    BlacklistCheckStatus.NotListed));
        }
    }

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

                                return pending.Task;
                            })
                    },
                    TestIp))
                .ToArray();

            var results = await Task.WhenAll(runs)
                .WaitAsync(TimeSpan.FromSeconds(8));

            Assert(
                Volatile.Read(ref startedCount) == 4,
                "Die Parallelitätsgrenze wurde nicht eingehalten.");

            Assert(
                results.All(item =>
                    item["psbl"].Status ==
                        BlacklistCheckStatus.Failed),
                "Blockierte Prüfer wurden falsch bewertet.");

            Assert(
                results.Count(item =>
                    item["psbl"].ErrorMessage?.Contains(
                        "Kein freier Prüfplatz",
                        StringComparison.OrdinalIgnoreCase) == true) == 1,
                "Fehlender Prüfplatz wurde nicht erkannt.");
        }
        finally
        {
            pending.TrySetResult(
                Result(
                    "psbl",
                    TestIp,
                    BlacklistCheckStatus.NotListed));
        }
    }

    private static async Task CheckCancellationDuringPendingCheckAsync()
    {
        var runner = new BlacklistBatchRunner(
            TimeSpan.FromSeconds(3));

        using var source = new CancellationTokenSource();

        var pending =
            new TaskCompletionSource<BlacklistCheckResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        var started =
            new TaskCompletionSource(
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

            await started.Task.WaitAsync(
                TimeSpan.FromSeconds(5));

            source.Cancel();

            await ExpectAsync<OperationCanceledException>(
                () => running.WaitAsync(
                    TimeSpan.FromSeconds(5)));
        }
        finally
        {
            pending.TrySetResult(
                Result(
                    "psbl",
                    TestIp,
                    BlacklistCheckStatus.NotListed));
        }
    }

    // ===================================================
    // DNSBL-Profiltests
    // ===================================================

    private static Task CheckDefinitionsAsync(
        IReadOnlyList<DnsBlacklistProfile> profiles,
        IReadOnlyList<ProfileExpectation> expectations)
    {
        Assert(
            profiles.Count == expectations.Count,
            "Die Anzahl der Anbieterprofile ist falsch.");

        Assert(
            profiles
                .Select(profile => profile.BlacklistId)
                .Distinct(StringComparer.Ordinal)
                .Count() == profiles.Count,
            "Die Anbieterkennungen sind nicht eindeutig.");

        var knownIds = BlacklistCatalog.Entries
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expectation in expectations)
        {
            var profile = profiles.SingleOrDefault(
                item => item.BlacklistId == expectation.Id);

            Assert(
                profile is not null,
                $"Anbieterprofil fehlt: {expectation.Id}");

            Assert(
                knownIds.Contains(profile.BlacklistId),
                $"Katalogeintrag fehlt: {expectation.Id}");

            Assert(
                profile.DnsZone == expectation.Zone,
                $"DNS-Zone ist falsch: {expectation.Id}");

            Assert(
                profile.PositiveControlAddress == "127.0.0.2",
                $"Positive Kontrolladresse ist falsch: {expectation.Id}");

            Assert(
                profile.NegativeControlAddress == "127.0.0.1",
                $"Negative Kontrolladresse ist falsch: {expectation.Id}");

            Assert(
                profile.ListedReturnCodes.Contains(
                    expectation.PositiveCode),
                $"Positiver Kontrollcode fehlt: {expectation.Id}");

            Assert(
                !profile.ListedReturnCodes.Intersect(
                    profile.AccessDeniedReturnCodes).Any(),
                "Listungs- und Sperrcodes überschneiden sich: " +
                expectation.Id);

            _ = new DnsBlacklistChecker(
                profile,
                new FakeResolver(
                    new Dictionary<
                        string,
                        DnsBlacklistDnsResponse>()));
        }

        return Task.CompletedTask;
    }

    private static async Task CheckProfilesAsync(
        IReadOnlyList<DnsBlacklistProfile> profiles,
        IReadOnlyList<ProfileExpectation> expectations,
        ProfileTestMode mode)
    {
        foreach (var expectation in expectations)
        {
            var profile = profiles.Single(
                item => item.BlacklistId == expectation.Id);

            var positive =
                $"2.0.0.127.{profile.DnsZone}";

            var negative =
                $"1.0.0.127.{profile.DnsZone}";

            var target =
                $"10.2.0.192.{profile.DnsZone}";

            var answers =
                new Dictionary<string, DnsBlacklistDnsResponse>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [positive] =
                        mode == ProfileTestMode.FailedControl
                            ? NxDomain
                            : Addresses(expectation.PositiveCode)
                };

            if (mode != ProfileTestMode.FailedControl)
            {
                answers.Add(
                    negative,
                    NxDomain);

                answers.Add(
                    target,
                    mode switch
                    {
                        ProfileTestMode.NotListed =>
                            NxDomain,

                        ProfileTestMode.Listed =>
                            Addresses(expectation.PositiveCode),

                        ProfileTestMode.Unknown =>
                            Addresses("127.0.0.254"),

                        _ => throw new InvalidOperationException(
                            "Unbekannter Profiltestmodus.")
                    });
            }

            var resolver = new FakeResolver(answers);

            var checker = new DnsBlacklistChecker(
                profile,
                resolver);

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

            Assert(
                result.Status == expectedStatus,
                $"{expectation.Id}: Erwartet {expectedStatus}, " +
                $"erhalten {result.Status}.");

            Assert(
                result.BlacklistId == expectation.Id,
                $"{expectation.Id}: Blacklist-Kennung falsch.");

            Assert(
                result.CheckedTarget == TestIp,
                $"{expectation.Id}: Prüfziel falsch.");

            Assert(
                result.IsConclusive ==
                    (mode is ProfileTestMode.NotListed
                        or ProfileTestMode.Listed),
                $"{expectation.Id}: IsConclusive falsch.");

            bool? expectedListed = mode switch
            {
                ProfileTestMode.NotListed => false,
                ProfileTestMode.Listed => true,
                _ => null
            };

            Assert(
                result.IsListed == expectedListed,
                $"{expectation.Id}: IsListed falsch.");

            var expectedQueries =
                mode == ProfileTestMode.FailedControl
                    ? new[] { positive }
                    : new[] { positive, negative, target };

            Assert(
                resolver.Queries.SequenceEqual(
                    expectedQueries,
                    StringComparer.OrdinalIgnoreCase),
                $"{expectation.Id}: DNS-Abfragen falsch.");

            Assert(
                result.StartedAtUtc.HasValue &&
                result.CompletedAtUtc.HasValue,
                $"{expectation.Id}: Zeitstempel fehlen.");
        }
    }

    private static async Task CheckBlocklistDeCodeAsync()
    {
        var profile = DnsBlacklistProviderProfiles.SecondBatch
            .Single(item => item.BlacklistId == "blocklist-de");

        Assert(
            profile.ListedReturnCodes.Contains("127.0.0.22"),
            "Der höhere blocklist.de-Listungscode fehlt.");

        Assert(
            profile.ListedReturnCodes.Count == 21,
            "Die Anzahl der blocklist.de-Rückgabecodes ist falsch.");

        var answers =
            new Dictionary<string, DnsBlacklistDnsResponse>
            {
                [$"2.0.0.127.{profile.DnsZone}"] =
                    Addresses("127.0.0.2"),

                [$"1.0.0.127.{profile.DnsZone}"] =
                    NxDomain,

                [$"10.2.0.192.{profile.DnsZone}"] =
                    Addresses("127.0.0.22")
            };

        var checker = new DnsBlacklistChecker(
            profile,
            new FakeResolver(answers));

        var result = await checker.CheckAsync(TestIp);

        Assert(
            result.Status == BlacklistCheckStatus.Listed,
            "Der höhere Listungscode wurde nicht erkannt.");

        Assert(
            result.IsConclusive &&
            result.IsListed == true,
            "Der Listungscode wurde nicht belastbar bewertet.");

        Assert(
            result.ProviderResponse == "127.0.0.22",
            "Der originale DNS-Rückgabecode fehlt.");
    }

    // ===================================================
    // Sprint 30.1 – Katalogtests
    // ===================================================

    private static Task CheckCatalogStructureAsync()
    {
        var entries = BlacklistCatalog.Entries;

        Assert(
            entries.Count == 68,
            $"Erwartet 68 Einträge, erhalten {entries.Count}.");

        Assert(
            entries.All(entry =>
                !string.IsNullOrWhiteSpace(entry.Id) &&
                !string.IsNullOrWhiteSpace(entry.DisplayName)),
            "Ein Katalogeintrag besitzt keine gültige Kennung " +
            "oder keinen Anzeigenamen.");

        Assert(
            entries.Select(entry => entry.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == entries.Count,
            "Der Katalog enthält doppelte Kennungen.");

        Assert(
            entries[0].Id == "ivmuri",
            "Die bisherige Katalogreihenfolge wurde verändert.");

        Assert(
            entries[^1].Id == "spfbl-dnsbl",
            "Der letzte Referenzeintrag wurde verändert.");

        return Task.CompletedTask;
    }

    private static Task CheckLegacyCatalogConstructorAsync()
    {
        var definition = new BlacklistDefinition(
            "legacy-test",
            "Legacy Test");

        Assert(
            definition.TargetKind ==
                BlacklistTargetKind.Unclassified,
            "Der Standardwert von TargetKind ist falsch.");

        Assert(
            definition.AccessStatus ==
                BlacklistAccessStatus.NotReviewed,
            "Der Standardwert von AccessStatus ist falsch.");

        Assert(
            definition.QueryMethod ==
                BlacklistQueryMethod.Unclassified,
            "Der Standardwert von QueryMethod ist falsch.");

        Assert(
            definition.ReputationScope ==
                BlacklistReputationScope.Unclassified,
            "Der Standardwert von ReputationScope ist falsch.");

        var existingDefinition = new BlacklistDefinition(
            "legacy-ip",
            "Legacy IP",
            BlacklistTargetKind.IpAddress,
            BlacklistAccessStatus.Permitted);

        Assert(
            existingDefinition.TargetKind ==
                BlacklistTargetKind.IpAddress &&
            existingDefinition.AccessStatus ==
                BlacklistAccessStatus.Permitted,
            "Der bisherige Vier-Parameter-Konstruktor " +
            "ist nicht mehr kompatibel.");

        Assert(
            existingDefinition.QueryMethod ==
                BlacklistQueryMethod.Unclassified &&
            existingDefinition.ReputationScope ==
                BlacklistReputationScope.Unclassified,
            "Optionale Metadaten besitzen falsche Standardwerte.");

        return Task.CompletedTask;
    }

    private static Task CheckActiveCatalogMetadataAsync()
    {
        Assert(
            ActiveBlacklistIds.Length == 7,
            "Es werden genau sieben aktive Referenzen erwartet.");

        Assert(
            ActiveBlacklistIds
                .Distinct(StringComparer.Ordinal)
                .Count() == 7,
            "Die aktiven Referenzen sind nicht eindeutig.");

        foreach (var id in ActiveBlacklistIds)
        {
            var definition = GetDefinition(id);

            Assert(
                definition.TargetKind ==
                    BlacklistTargetKind.IpAddress,
                $"{id}: Falsches Prüfobjekt.");

            Assert(
                definition.QueryMethod ==
                    BlacklistQueryMethod.Dns,
                $"{id}: Falsches Abfrageverfahren.");

            Assert(
                definition.AccessStatus ==
                    BlacklistAccessStatus.Permitted,
                $"{id}: Unerwarteter Zugangsstatus.");

            Assert(
                definition.ReputationScope !=
                    BlacklistReputationScope.Unclassified,
                $"{id}: Bewertungsumfang fehlt.");
        }

        return Task.CompletedTask;
    }

    private static Task CheckDomainAndUriMetadataAsync()
    {
        var fresh = GetDefinition("sem-fresh");
        var semUri = GetDefinition("sem-uri");
        var semUriRed = GetDefinition("sem-urired");
        var spamhausDbl = GetDefinition("spamhaus-dbl");
        var surbl = GetDefinition("surbl-multi");
        var ivmUri = GetDefinition("ivmuri");

        Assert(
            fresh.TargetKind == BlacklistTargetKind.Domain &&
            fresh.ReputationScope ==
                BlacklistReputationScope.Domain,
            "SEM FRESH wurde falsch klassifiziert.");

        Assert(
            semUri.TargetKind ==
                BlacklistTargetKind.DomainFromUri &&
            semUri.ReputationScope ==
                BlacklistReputationScope.UriDomain,
            "SEM URI wurde falsch klassifiziert.");

        Assert(
            semUriRed.TargetKind ==
                BlacklistTargetKind.DomainFromUri &&
            semUriRed.ReputationScope ==
                BlacklistReputationScope.UriDomain,
            "SEM URIRED wurde falsch klassifiziert.");

        Assert(
            spamhausDbl.TargetKind ==
                BlacklistTargetKind.Domain &&
            spamhausDbl.ReputationScope ==
                BlacklistReputationScope.Domain,
            "Spamhaus DBL darf nicht als IP-Prüfung " +
            "klassifiziert werden.");

        Assert(
            surbl.TargetKind ==
                BlacklistTargetKind.DomainFromUri &&
            surbl.ReputationScope ==
                BlacklistReputationScope.UriDomain,
            "SURBL Multi wurde falsch klassifiziert.");

        Assert(
            ivmUri.TargetKind ==
                BlacklistTargetKind.Uri &&
            ivmUri.ReputationScope ==
                BlacklistReputationScope.Mixed,
            "ivmURI wurde falsch klassifiziert.");

        foreach (var definition in new[]
                 {
                     fresh,
                     semUri,
                     semUriRed,
                     spamhausDbl,
                     surbl,
                     ivmUri
                 })
        {
            Assert(
                definition.QueryMethod ==
                    BlacklistQueryMethod.Dns,
                $"{definition.Id}: DNS-Abfrageverfahren fehlt.");
        }

        Assert(
            spamhausDbl.AccessStatus ==
                BlacklistAccessStatus.RequiresProviderAuthorization &&
            surbl.AccessStatus ==
                BlacklistAccessStatus.RequiresProviderAuthorization &&
            ivmUri.AccessStatus ==
                BlacklistAccessStatus.RequiresProviderAuthorization,
            "Die Zugangsvoraussetzungen wurden verändert.");

        return Task.CompletedTask;
    }

    private static Task CheckUceProtectScopesAsync()
    {
        var level1 = GetDefinition("uceprotectl1");
        var level2 = GetDefinition("uceprotectl2");
        var level3 = GetDefinition("uceprotectl3");

        Assert(
            level1.TargetKind == BlacklistTargetKind.IpAddress &&
            level2.TargetKind == BlacklistTargetKind.IpAddress &&
            level3.TargetKind == BlacklistTargetKind.IpAddress,
            "Alle drei Level müssen IPv4-Prüfobjekte verwenden.");

        Assert(
            level1.ReputationScope ==
                BlacklistReputationScope.SingleIp,
            "Level 1 muss die Einzel-IP bewerten.");

        Assert(
            level2.ReputationScope ==
                BlacklistReputationScope.IpNetwork,
            "Level 2 muss als Netzbewertung " +
            "klassifiziert werden.");

        Assert(
            level3.ReputationScope ==
                BlacklistReputationScope.AutonomousSystem,
            "Level 3 muss als ASN-Bewertung " +
            "klassifiziert werden.");

        return Task.CompletedTask;
    }

    private static Task CheckDiscontinuedSourcesAsync()
    {
        var badConf = GetDefinition(
            "sorbs-rhsbl-badconf");

        var noMail = GetDefinition(
            "sorbs-rhsbl-nomail");

        foreach (var definition in new[]
                 {
                     badConf,
                     noMail
                 })
        {
            Assert(
                definition.AccessStatus ==
                    BlacklistAccessStatus.Discontinued,
                $"{definition.Id}: Einstellungsstatus fehlt.");

            Assert(
                definition.TargetKind ==
                    BlacklistTargetKind.Domain,
                $"{definition.Id}: Historisches Prüfobjekt falsch.");

            Assert(
                definition.QueryMethod ==
                    BlacklistQueryMethod.Dns,
                $"{definition.Id}: Historisches Verfahren falsch.");
        }

        var emptyResults =
            new Dictionary<string, BlacklistCheckResult>(
                StringComparer.Ordinal);

        var overview = BlacklistOverviewFactory.Create(
            emptyResults);

        var sorbsEntries = overview.Entries
            .Where(entry =>
                entry.Id is
                    "sorbs-rhsbl-badconf"
                    or "sorbs-rhsbl-nomail")
            .ToArray();

        Assert(
            sorbsEntries.Length == 2,
            "Die historischen SORBS-Einträge fehlen.");

        Assert(
            sorbsEntries.All(entry =>
                entry.Status == "NotIntegrated" &&
                !entry.IsConclusive &&
                entry.IsListed is null),
            "Eingestellte Quellen wurden ohne Prüfergebnis " +
            "als erfolgreich bewertet.");

        return Task.CompletedTask;
    }

    private static Task CheckCatalogOverviewAsync()
    {
        var now = DateTimeOffset.UtcNow;

        var results =
            new Dictionary<string, BlacklistCheckResult>(
                StringComparer.Ordinal);

        foreach (var id in ActiveBlacklistIds)
        {
            results.Add(
                id,
                new BlacklistCheckResult
                {
                    BlacklistId = id,
                    CheckedTarget = TestIp,
                    Status = BlacklistCheckStatus.NotListed,
                    StartedAtUtc = now,
                    CompletedAtUtc = now,
                    ProviderResponse = "NXDOMAIN"
                });
        }

        var overview = BlacklistOverviewFactory.Create(
            results);

        Assert(
            overview.TotalCount == 68,
            "Die Übersicht enthält nicht 68 Referenzeinträge.");

        Assert(
            overview.IntegratedCount == 7,
            "Die sieben simulierten Ergebnisse " +
            "wurden nicht korrekt integriert.");

        Assert(
            overview.NotIntegratedCount == 61,
            "Die übrigen 61 Quellen wurden " +
            "nicht korrekt als offen gekennzeichnet.");

        Assert(
            overview.NotListedCount == 7,
            "Die sieben negativen Ergebnisse " +
            "wurden falsch gezählt.");

        Assert(
            overview.ListedCount == 0,
            "Es wurde eine nicht vorhandene " +
            "Listung gezählt.");

        Assert(
            overview.WarningCount == 0,
            "Es wurde eine nicht vorhandene " +
            "Warnung gezählt.");

        foreach (var id in ActiveBlacklistIds)
        {
            var entry = overview.Entries.Single(
                item => item.Id == id);

            Assert(
                entry.Status == "NotListed" &&
                entry.IsConclusive &&
                entry.IsListed == false,
                $"{id}: Simulierter Negativbefund falsch.");

            Assert(
                entry.CheckedTarget == TestIp,
                $"{id}: Prüfobjekt wurde nicht übernommen.");

            Assert(
                entry.ProviderResponse == "NXDOMAIN",
                $"{id}: Anbieterantwort wurde nicht übernommen.");

            Assert(
                entry.TargetKind ==
                    BlacklistTargetKind.IpAddress.ToString(),
                $"{id}: Alte API-Metadaten sind nicht kompatibel.");
        }

        var spamhausEntry = overview.Entries.Single(
            item => item.Id == "spamhaus-dbl");

        Assert(
            spamhausEntry.Status == "NotIntegrated" &&
            !spamhausEntry.IsConclusive &&
            spamhausEntry.IsListed is null,
            "Eine nicht integrierte Domain-Blacklist " +
            "wurde fälschlich als geprüft ausgewiesen.");

        return Task.CompletedTask;
    }

    // ===================================================
    // Sprint 30.2 – Referenz-Regressionstest
    // ===================================================

    private static Task CheckCompleteReferenceSetAsync()
    {
        Assert(
            ExpectedReferenceIds.Length == 68,
            "Die feste Referenzliste muss 68 Einträge besitzen.");

        var currentIds = BlacklistCatalog.Entries
            .Select(entry => entry.Id)
            .ToArray();

        Assert(
            currentIds.Length == ExpectedReferenceIds.Length,
            "Katalog und Referenzliste haben " +
            "unterschiedliche Eintragszahlen.");

        Assert(
            currentIds.SequenceEqual(
                ExpectedReferenceIds,
                StringComparer.Ordinal),
            "Die exakten 68 Referenzkennungen " +
            "oder ihre Reihenfolge wurden verändert.");

        return Task.CompletedTask;
    }

    // ===================================================
    // Sprint 30.2–30.6 – Metadatenprüfungen
    // ===================================================

    /// <summary>
    /// Gemeinsame Regression für IPv4-DNSBL-Einträge.
    ///
    /// Die individuellen Erwartungswerte werden
    /// für jeden Anbieter separat festgelegt.
    ///
    /// Kein Netzwerkzugriff.
    /// </summary>
    private static Task CheckIpProviderMetadataAsync(
        CatalogMetadataExpectation expectation)
    {
        var definition = GetDefinition(expectation.Id);

        Assert(
            definition.TargetKind ==
                BlacklistTargetKind.IpAddress,
            $"{expectation.Id}: Erwartet wird " +
            "ein IPv4-Prüfobjekt.");

        Assert(
            definition.QueryMethod ==
                BlacklistQueryMethod.Dns,
            $"{expectation.Id}: Erwartet wird " +
            "eine DNS-basierte Abfrage.");

        Assert(
            definition.AccessStatus ==
                expectation.AccessStatus,
            $"{expectation.Id}: Der Zugangsstatus " +
            "entspricht nicht der Klassifizierung.");

        Assert(
            definition.ReputationScope ==
                expectation.ReputationScope,
            $"{expectation.Id}: Der Bewertungsumfang " +
            "entspricht nicht der Klassifizierung.");

        return Task.CompletedTask;
    }

    private static Task CheckSprint302InactiveAsync()
    {
        var currentResults =
            new Dictionary<string, BlacklistCheckResult>(
                StringComparer.Ordinal);

        foreach (var id in ActiveBlacklistIds)
        {
            currentResults.Add(
                id,
                Result(
                    id,
                    TestIp,
                    BlacklistCheckStatus.NotListed));
        }

        var overview = BlacklistOverviewFactory.Create(
            currentResults);

        Assert(
            overview.IntegratedCount == 7,
            "Die simulierte Übersicht muss " +
            "sieben integrierte Listen enthalten.");

        Assert(
            overview.NotIntegratedCount == 61,
            "Die simulierte Übersicht muss " +
            "61 nicht integrierte Listen enthalten.");

        foreach (var expectation in Sprint302Expectations)
        {
            Assert(
                !ActiveBlacklistIds.Contains(
                    expectation.Id,
                    StringComparer.Ordinal),
                $"{expectation.Id}: Ein neues Anbieterprofil " +
                "wurde versehentlich in die aktive " +
                "Referenzliste aufgenommen.");

            var entry = overview.Entries.Single(
                item => item.Id == expectation.Id);

            Assert(
                entry.Status == "NotIntegrated",
                $"{expectation.Id}: Anbieter darf ohne " +
                "Prüfergebnis nicht als integriert gelten.");

            Assert(
                !entry.IsConclusive &&
                entry.IsListed is null,
                $"{expectation.Id}: Es darf kein " +
                "fiktives Prüfergebnis erscheinen.");

            Assert(
                entry.AccessStatus ==
                    expectation.AccessStatus.ToString(),
                $"{expectation.Id}: Der Zugangsstatus " +
                "wird in der Übersicht falsch ausgegeben.");
        }

        return Task.CompletedTask;
    }

    // ===================================================
    // Sprint 30.6 – Vollständigkeit und Isolation
    // ===================================================

    private static Task CheckAllSourcesClassifiedAsync()
    {
        var entries = BlacklistCatalog.Entries;

        Assert(
            entries.Count == 68,
            "Der Referenzkatalog muss weiterhin " +
            "exakt 68 Einträge enthalten.");

        var unclassified = entries
            .Where(entry =>
                entry.TargetKind ==
                    BlacklistTargetKind.Unclassified ||
                entry.QueryMethod ==
                    BlacklistQueryMethod.Unclassified ||
                entry.ReputationScope ==
                    BlacklistReputationScope.Unclassified)
            .Select(entry => entry.Id)
            .ToArray();

        Assert(
            unclassified.Length == 0,
            "Technisch unklassifizierte Quellen: " +
            string.Join(", ", unclassified));

        Assert(
            Sprint306Expectations.Length == 15,
            "Sprint 30.6 muss genau 15 Quellen absichern.");

        Assert(
            Sprint306Expectations
                .Select(expectation => expectation.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() == 15,
            "Sprint 30.6 enthält doppelte Referenzkennungen.");

        return Task.CompletedTask;
    }

    private static Task CheckSprint306NotIntegratedAsync()
    {
        var currentResults =
            new Dictionary<string, BlacklistCheckResult>(
                StringComparer.Ordinal);

        foreach (var id in ActiveBlacklistIds)
        {
            currentResults.Add(
                id,
                Result(
                    id,
                    TestIp,
                    BlacklistCheckStatus.NotListed));
        }

        var overview = BlacklistOverviewFactory.Create(
            currentResults);

        Assert(
            overview.TotalCount == 68,
            "Die Übersicht muss 68 Referenzen enthalten.");

        Assert(
            overview.IntegratedCount == 7,
            "Es dürfen nur sieben Quellen als " +
            "integriert ausgewiesen werden.");

        Assert(
            overview.NotIntegratedCount == 61,
            "Es müssen weiterhin 61 Quellen " +
            "als nicht integriert erscheinen.");

        Assert(
            overview.NotListedCount == 7,
            "Es dürfen nur sieben simulierte " +
            "Negativbefunde vorliegen.");

        foreach (var expectation in Sprint306Expectations)
        {
            Assert(
                !ActiveBlacklistIds.Contains(
                    expectation.Id,
                    StringComparer.Ordinal),
                $"{expectation.Id}: Die Quelle wurde " +
                "unerwartet als aktiv registriert.");

            var entry = overview.Entries.Single(
                item => item.Id == expectation.Id);

            Assert(
                entry.Status == "NotIntegrated",
                $"{expectation.Id}: Ohne tatsächliches " +
                "Prüfergebnis muss NotIntegrated gelten.");

            Assert(
                !entry.IsConclusive &&
                entry.IsListed is null,
                $"{expectation.Id}: Ein nicht ausgeführter " +
                "Check darf weder als gelistet noch " +
                "als nicht gelistet erscheinen.");

            Assert(
                entry.AccessStatus ==
                    expectation.AccessStatus.ToString(),
                $"{expectation.Id}: Der Zugangsstatus " +
                "wird in der Übersicht falsch ausgegeben.");
        }

        return Task.CompletedTask;
    }

    // ===================================================
    // Gemeinsame Hilfsfunktionen
    // ===================================================

    private static BlacklistDefinition GetDefinition(
        string id)
    {
        return BlacklistCatalog.Entries.Single(
            entry => entry.Id == id);
    }

    private static string ModeName(ProfileTestMode mode)
    {
        return mode switch
        {
            ProfileTestMode.NotListed =>
                "Nicht gelistet",

            ProfileTestMode.Listed =>
                "Gelistet",

            ProfileTestMode.Unknown =>
                "Unbekannter Antwortcode",

            ProfileTestMode.FailedControl =>
                "Defekte Positivkontrolle",

            _ => throw new ArgumentOutOfRangeException(
                nameof(mode))
        };
    }

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

                return Task.FromResult(
                    new BlacklistCheckResult
                    {
                        BlacklistId = actualId ?? id,
                        CheckedTarget = actualTarget ?? target,
                        Status = status,

                        StartedAtUtc =
                            withTimestamps ? now : null,

                        CompletedAtUtc =
                            withTimestamps ? now : null
                    });
            });
    }

    private static BlacklistCheckResult Result(
        string id,
        string target,
        BlacklistCheckStatus status)
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
            AccessDeniedReturnCodes:
                new[] { "127.255.255.254" },
            PositiveControlAddress: "127.0.0.2",
            NegativeControlAddress: "127.0.0.1");
    }

    private static DnsBlacklistDnsResponse Addresses(
        params string[] values)
    {
        return new DnsBlacklistDnsResponse(
            IsNxDomain: false,
            Addresses: values
                .Select(IPAddress.Parse)
                .ToArray());
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
            "Erwartete Ausnahme " +
            $"{typeof(TException).Name} fehlt.");
    }

    private static void Assert(
        [DoesNotReturnIf(false)] bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
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

    private sealed record CatalogMetadataExpectation(
        string Id,
        BlacklistAccessStatus AccessStatus,
        BlacklistReputationScope ReputationScope);

    private enum ProfileTestMode
    {
        NotListed,
        Listed,
        Unknown,
        FailedControl
    }

    private sealed class FakeResolver : IDnsBlacklistResolver
    {
        private readonly IReadOnlyDictionary<
            string,
            DnsBlacklistDnsResponse> _answers;

        public List<string> Queries { get; } = new();

        public FakeResolver(
            IReadOnlyDictionary<
                string,
                DnsBlacklistDnsResponse> answers)
        {
            _answers = answers;
        }

        public Task<DnsBlacklistDnsResponse> QueryAsync(
            string name,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Queries.Add(name);

            if (!_answers.TryGetValue(
                    name,
                    out var answer))
            {
                throw new InvalidOperationException(
                    "Simulierte DNS-Antwort fehlt: " + name);
            }

            return Task.FromResult(answer);
        }
    }
}
