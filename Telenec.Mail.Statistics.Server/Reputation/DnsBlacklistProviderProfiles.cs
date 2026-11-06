
namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Technische DNSBL-Profile der ersten Anbietergruppe.
///
/// Die Profile sind zunächst ausschließlich
/// Konfigurationsdaten.
///
/// Sie starten keine DNS-Abfragen und
/// aktivieren keine Anbieter im Monitoring.
///
/// Vor der produktiven Aktivierung werden
/// die Profile automatisiert getestet und
/// die Kontrollabfragen live überprüft.
/// </summary>
public static class DnsBlacklistProviderProfiles
{
    /*
     * 0spam Hauptliste:
     *
     * 127.0.0.1 bis 127.0.0.9:
     * Verschiedene Listungsgründe für Einzel-IPs.
     *
     * 127.0.1.0 bis 127.0.9.0:
     * Entsprechende Kategorien auf Netzebene.
     *
     * Andere Antwortcodes bleiben Unknown.
     */
    private static readonly IReadOnlyCollection<string>
        ZeroSpamMainCodes =
            Enumerable.Range(1, 9)
                .Select(number => $"127.0.0.{number}")
                .Concat(
                    Enumerable.Range(1, 9)
                        .Select(number =>
                            $"127.0.{number}.0"))
                .ToArray();

    public static IReadOnlyList<DnsBlacklistProfile>
        FirstBatch
    { get; } =
            Array.AsReadOnly(
                new DnsBlacklistProfile[]
                {
                    /*
                     * 0spam Main DNSBL
                     */
                    new(
                        BlacklistId: "0spam",
                        DnsZone: "bl.0spam.org",
                        ListedReturnCodes:
                            ZeroSpamMainCodes,
                        AccessDeniedReturnCodes:
                            Array.Empty<string>(),
                        PositiveControlAddress: "127.0.0.2",
                        NegativeControlAddress: "127.0.0.1"),

                    /*
                     * 0spam Realtime Blacklist
                     *
                     * Der Testcode 127.0.0.2
                     * sowie 127.0.0.15 wurden
                     * als Anbieterantworten
                     * dokumentiert.
                     *
                     * Weitere nicht bestätigte
                     * Codes bleiben Unknown.
                     */
                    new(
                        BlacklistId: "0spam-rbl",
                        DnsZone: "rbl.0spam.org",
                        ListedReturnCodes:
                            new[]
                            {
                                "127.0.0.2",
                                "127.0.0.15"
                            },
                        AccessDeniedReturnCodes:
                            Array.Empty<string>(),
                        PositiveControlAddress: "127.0.0.2",
                        NegativeControlAddress: "127.0.0.1"),

                    /*
                     * DroneBL
                     *
                     * 127.0.0.1 ist der bekannte
                     * positive Testeintrag.
                     *
                     * 127.0.0.255 wird bewusst
                     * nicht als bestätigter
                     * Listungscode akzeptiert.
                     *
                     * Die weiteren Codes bilden
                     * dokumentierte Kategorien
                     * von Missbrauchssystemen ab.
                     */
                    new(
                        BlacklistId: "drone-bl",
                        DnsZone: "dnsbl.dronebl.org",
                        ListedReturnCodes:
                            new[]
                            {
                                "127.0.0.1",
                                "127.0.0.2",
                                "127.0.0.3",
                                "127.0.0.5",
                                "127.0.0.6",
                                "127.0.0.7",
                                "127.0.0.8",
                                "127.0.0.9",
                                "127.0.0.10",
                                "127.0.0.11",
                                "127.0.0.12",
                                "127.0.0.13",
                                "127.0.0.14",
                                "127.0.0.15",
                                "127.0.0.16",
                                "127.0.0.17",
                                "127.0.0.18"
                            },
                        AccessDeniedReturnCodes:
                            Array.Empty<string>(),
                        PositiveControlAddress: "127.0.0.2",
                        NegativeControlAddress: "127.0.0.1"),

                    /*
                     * UCEPROTECT Level 1
                     *
                     * Prüft einzelne IPv4-Adressen.
                     * Level 2 und 3 sind separate
                     * Reputationsquellen.
                     */
                    new(
                        BlacklistId: "uceprotectl1",
                        DnsZone: "dnsbl-1.uceprotect.net",
                        ListedReturnCodes:
                            new[]
                            {
                                "127.0.0.2"
                            },
                        AccessDeniedReturnCodes:
                            Array.Empty<string>(),
                        PositiveControlAddress: "127.0.0.2",
                        NegativeControlAddress: "127.0.0.1")
                });
}
