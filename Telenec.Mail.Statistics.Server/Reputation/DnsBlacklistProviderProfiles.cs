
namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Zentrale DNSBL-Anbieterprofile.
///
/// Die Profile enthalten ausschließlich technische
/// Konfigurationsdaten.
///
/// Das Anlegen eines Profils aktiviert keine
/// externen Abfragen.
///
/// Aktive Anbieter werden ausschließlich im
/// BlacklistMonitoringService registriert.
///
/// Vor einer produktiven Aktivierung müssen
/// Rückgabecodes, Kontrolladressen und
/// Nutzungsbedingungen geprüft sein.
/// </summary>
public static class DnsBlacklistProviderProfiles
{
    /*
     * 0spam:
     *
     * Unterschiedliche Rückgabecodes für
     * Einzeladressen und Netzbereiche.
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

    /*
     * blocklist.de:
     *
     * Rückgabecodes 127.0.0.2 bis
     * 127.0.0.22 kennzeichnen
     * dokumentierte Angriffskategorien.
     *
     * Beispielsweise:
     * 127.0.0.2  amavis
     * 127.0.0.9  mail
     * 127.0.0.14 ssh
     * 127.0.0.15 w00tw00t
     *
     * Der kontrollierte Testeintrag
     * liefert aktuell 127.0.0.2.
     */
    private static readonly IReadOnlyCollection<string>
        BlocklistDeCodes =
            Enumerable.Range(2, 21)
                .Select(number => $"127.0.0.{number}")
                .ToArray();

    /// <summary>
    /// Erste Anbietergruppe.
    ///
    /// PSBL wird separat integriert.
    ///
    /// Aktuell produktiv registriert:
    /// - 0spam
    /// - 0spam RBL
    /// - UCEPROTECT Level 1
    ///
    /// DroneBL bleibt vorbereitet, aber
    /// wegen der lokalen DNS-Abweichung
    /// noch deaktiviert.
    /// </summary>
    public static IReadOnlyList<DnsBlacklistProfile>
        FirstBatch
    { get; } =
            Array.AsReadOnly(
                new DnsBlacklistProfile[]
                {
                    new(
                        BlacklistId: "0spam",
                        DnsZone: "bl.0spam.org",
                        ListedReturnCodes:
                            ZeroSpamMainCodes,
                        AccessDeniedReturnCodes:
                            Array.Empty<string>(),
                        PositiveControlAddress: "127.0.0.2",
                        NegativeControlAddress: "127.0.0.1"),

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
                     * DroneBL:
                     *
                     * Noch nicht aktiv.
                     * Der lokale Windows-Resolver
                     * konnte den positiven Testeintrag
                     * bislang nicht auflösen.
                     *
                     * Öffentliche Resolver lieferten
                     * den erwarteten Wert 127.0.0.1.
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

    /// <summary>
    /// Zweite Anbietergruppe.
    ///
    /// Alle vier DNS-Zonen haben die
    /// grundlegenden Positiv- und
    /// Negativkontrollen über die
    /// .NET-Systemauflösung bestanden.
    ///
    /// Diese Profile sind derzeit
    /// noch NICHT produktiv registriert.
    /// </summary>
    public static IReadOnlyList<DnsBlacklistProfile>
        SecondBatch
    { get; } =
            Array.AsReadOnly(
                new DnsBlacklistProfile[]
                {
                    /*
                     * blocklist.de
                     *
                     * Berücksichtigt verschiedene
                     * dokumentierte Angriffskategorien.
                     *
                     * Ein Treffer muss nicht zwingend
                     * auf Spamversand zurückgehen.
                     */
                    new(
                        BlacklistId: "blocklist-de",
                        DnsZone: "bl.blocklist.de",
                        ListedReturnCodes:
                            BlocklistDeCodes,
                        AccessDeniedReturnCodes:
                            Array.Empty<string>(),
                        PositiveControlAddress: "127.0.0.2",
                        NegativeControlAddress: "127.0.0.1"),

                    /*
                     * SpamCop Blocking List
                     *
                     * IP-basierte Reputationsliste
                     * für gemeldeten Spamversand.
                     *
                     * Dokumentierter Rückgabecode:
                     * 127.0.0.2
                     */
                    new(
                        BlacklistId: "spamcop",
                        DnsZone: "bl.spamcop.net",
                        ListedReturnCodes:
                            new[]
                            {
                                "127.0.0.2"
                            },
                        AccessDeniedReturnCodes:
                            Array.Empty<string>(),
                        PositiveControlAddress: "127.0.0.2",
                        NegativeControlAddress: "127.0.0.1"),

                    /*
                     * UCEPROTECT Level 2
                     *
                     * Prüft die Reputation
                     * größerer IP-Netzbereiche.
                     *
                     * Ein Listungstreffer bedeutet
                     * nicht zwangsläufig, dass
                     * die konkrete Versand-IP
                     * selbst Spam verschickt hat.
                     */
                    new(
                        BlacklistId: "uceprotectl2",
                        DnsZone: "dnsbl-2.uceprotect.net",
                        ListedReturnCodes:
                            new[]
                            {
                                "127.0.0.2"
                            },
                        AccessDeniedReturnCodes:
                            Array.Empty<string>(),
                        PositiveControlAddress: "127.0.0.2",
                        NegativeControlAddress: "127.0.0.1"),

                    /*
                     * UCEPROTECT Level 3
                     *
                     * Bewertet die Reputation
                     * ganzer autonomer Systeme.
                     *
                     * Treffer müssen später
                     * entsprechend kenntlich
                     * gemacht werden.
                     */
                    new(
                        BlacklistId: "uceprotectl3",
                        DnsZone: "dnsbl-3.uceprotect.net",
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
