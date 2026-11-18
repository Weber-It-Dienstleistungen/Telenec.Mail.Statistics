
namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Erweiterte IPv4-DNSBL-Anbieterprofile.
///
/// PublicDnsblProfiles:
/// Anbieter für den regulären Sammeltest.
///
/// ReviewDnsblProfiles:
/// Weitere Kandidaten, nur nach ausdrücklicher
/// Aktivierung über die Konfiguration.
///
/// DeferredDnsblProfiles:
/// Zurückgestellte Anbieter mit besonderen
/// technischen Prüfanforderungen.
///
/// Keine dieser Definitionen startet selbst
/// externe DNS-Abfragen.
/// </summary>
public static class ExtendedDnsBlacklistProviderProfiles
{
    /// <summary>
    /// Übliche Zugriffssperrcodes.
    ///
    /// WICHTIG:
    /// Diese Sammlung muss VOR den Profilen
    /// initialisiert werden, da sie bereits
    /// beim Erzeugen der Profile verwendet wird.
    ///
    /// Zugriffssperrcodes dürfen niemals
    /// als bestätigte Listung gewertet werden.
    /// </summary>
    private static readonly IReadOnlyCollection<string>
        AccessDeniedCodes =
            Array.AsReadOnly(
                new[]
                {
                    "127.255.255.254",
                    "127.255.255.255"
                });

    /// <summary>
    /// Reguläre Erweiterungsgruppe.
    ///
    /// Dreizehn Anbieter.
    ///
    /// Die Gruppenfreigabe erfolgt über
    /// Reputation:EnableExtendedDnsbl.
    /// </summary>
    public static IReadOnlyList<DnsBlacklistProfile>
        PublicDnsblProfiles
    { get; } =
        Array.AsReadOnly(
            new DnsBlacklistProfile[]
            {
                /*
                 * Team Cymru:
                 * Netzreputation, keine klassische
                 * Spamversandbewertung.
                 */
                Create(
                    "cymru-bogons",
                    "bogons.cymru.com",
                    "192.168.1.1",
                    "8.8.8.8",
                    "127.0.0.2"),

                /*
                 * Dan's TOR:
                 * Allgemeine TOR-Knoten.
                 */
                CreateStandard(
                    "dan-tor",
                    "tor.dan.me.uk",
                    "127.0.0.100"),

                /*
                 * Dan's TOR Exit:
                 * TOR-Ausgangsknoten.
                 */
                CreateStandard(
                    "dan-torexit",
                    "torexit.dan.me.uk",
                    "127.0.0.100"),

                /*
                 * Fabel Spamsources.
                 */
                CreateStandard(
                    "fabelsources",
                    "spamsources.fabel.dk",
                    "127.0.0.2"),

                /*
                 * SCHULTE.
                 */
                CreateStandard(
                    "schulte",
                    "rbl.schulte.org",
                    "127.0.0.2"),

                /*
                 * ZapBL.
                 */
                CreateStandard(
                    "zapbl",
                    "dnsbl.zapbl.net",
                    "127.0.0.2"),

                /*
                 * SpamCop.
                 */
                CreateStandard(
                    "spamcop",
                    "bl.spamcop.net",
                    "127.0.0.2"),

                /*
                 * MAILSPIKE BL.
                 *
                 * Die positive Kontrolladresse
                 * liefert mehrere Reputationscodes.
                 *
                 * 127.0.0.13 (L2) ist lediglich
                 * eine verdächtige Reputation.
                 *
                 * Dieser Code ist für die positive
                 * Kontrollabfrage zugelassen,
                 * aber kein echter Listungscode.
                 */
                new DnsBlacklistProfile(
                    BlacklistId: "mailspike-bl",
                    DnsZone: "bl.mailspike.net",
                    ListedReturnCodes:
                        Array.AsReadOnly(
                            new[]
                            {
                                "127.0.0.2",
                                "127.0.0.10",
                                "127.0.0.11",
                                "127.0.0.12"
                            }),
                    AccessDeniedReturnCodes:
                        AccessDeniedCodes,
                    PositiveControlAddress: "127.0.0.2",
                    NegativeControlAddress: "127.0.0.1",
                    PositiveControlAdditionalReturnCodes:
                        Array.AsReadOnly(
                            new[]
                            {
                                "127.0.0.13"
                            })),

                /*
                 * MAILSPIKE Z.
                 */
                CreateStandard(
                    "mailspike-z",
                    "z.mailspike.net",
                    "127.0.0.2"),

                /*
                 * Suomispam Reputation.
                 */
                CreateStandard(
                    "suomispam-reputation",
                    "bl.suomispam.net",
                    "127.0.0.2",
                    "127.0.0.3",
                    "127.0.0.4",
                    "127.0.0.5",
                    "127.0.0.99"),

                /*
                 * Backscatterer.
                 */
                CreateStandard(
                    "backscatterer",
                    "ips.backscatterer.org",
                    "127.0.0.2"),

                /*
                 * NoSolicitado.
                 */
                CreateStandard(
                    "nosolicitado",
                    "bl.nosolicitado.org",
                    "127.0.0.2"),

                /*
                 * SPFBL DNSBL.
                 *
                 * 127.0.0.4 wird nicht als
                 * allgemeiner Spamtreffer gewertet.
                 */
                CreateStandard(
                    "spfbl-dnsbl",
                    "dnsbl.spfbl.net",
                    "127.0.0.2",
                    "127.0.0.3",
                    "127.0.0.5")
            });

    /// <summary>
    /// Weitere fünf DNSBL-Kandidaten.
    ///
    /// Werden erst mit gesonderter
    /// Konfigurationsfreigabe zugeschaltet.
    ///
    /// Bisher keine bestätigten produktiven
    /// Positiv- und Negativkontrollen.
    /// </summary>
    public static IReadOnlyList<DnsBlacklistProfile>
        ReviewDnsblProfiles
    { get; } =
        Array.AsReadOnly(
            new DnsBlacklistProfile[]
            {
                CreateStandard(
                    "anonmails-dnsbl",
                    "spam.dnsbl.anonmails.de",
                    "127.0.0.2"),

                CreateStandard(
                    "s5h-net",
                    "all.s5h.net",
                    "127.0.0.2"),

                CreateStandard(
                    "interserver",
                    "rbl.interserver.net",
                    "127.0.0.2"),

                CreateStandard(
                    "sem-backscatter",
                    "backscatter.spameatingmonkey.net",
                    "127.0.0.2"),

                CreateStandard(
                    "sem-black",
                    "bl.spameatingmonkey.net",
                    "127.0.0.2")
            });

    /// <summary>
    /// Zurückgestellte Quellen.
    ///
    /// Diese Profile werden vom regulären
    /// BlacklistMonitoringService NICHT
    /// automatisch registriert.
    ///
    /// HostKarma:
    /// Unterschiedliche Reputationscodes
    /// einschließlich Whitelist-Einträgen.
    ///
    /// IMP SPAM:
    /// Positive Kontrollabfrage bisher
    /// nicht zuverlässig bestätigt.
    ///
    /// Nur zur technischen Dokumentation.
    /// </summary>
    public static IReadOnlyList<DnsBlacklistProfile>
        DeferredDnsblProfiles
    { get; } =
        Array.AsReadOnly(
            new DnsBlacklistProfile[]
            {
                CreateStandard(
                    "hostkarma-black",
                    "hostkarma.junkemailfilter.com",
                    "127.0.0.2"),

                CreateStandard(
                    "imp-spam",
                    "spamrbl.imp.ch",
                    "127.0.0.2")
            });

    /// <summary>
    /// Reguläres IPv4-DNSBL-Profil
    /// mit Standard-Kontrolladressen.
    /// </summary>
    private static DnsBlacklistProfile CreateStandard(
        string blacklistId,
        string dnsZone,
        params string[] listedReturnCodes)
    {
        return Create(
            blacklistId,
            dnsZone,
            "127.0.0.2",
            "127.0.0.1",
            listedReturnCodes);
    }

    /// <summary>
    /// IPv4-DNSBL-Profil mit individuell
    /// definierten Kontrolladressen.
    /// </summary>
    private static DnsBlacklistProfile Create(
        string blacklistId,
        string dnsZone,
        string positiveControlAddress,
        string negativeControlAddress,
        params string[] listedReturnCodes)
    {
        return new DnsBlacklistProfile(
            BlacklistId: blacklistId,
            DnsZone: dnsZone,
            ListedReturnCodes:
                Array.AsReadOnly(listedReturnCodes),
            AccessDeniedReturnCodes:
                AccessDeniedCodes,
            PositiveControlAddress:
                positiveControlAddress,
            NegativeControlAddress:
                negativeControlAddress);
    }
}
