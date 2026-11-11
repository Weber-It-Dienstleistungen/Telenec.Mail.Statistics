
namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Art des Eingabeobjekts einer Reputationsprüfung.
/// </summary>
public enum BlacklistTargetKind
{
    Unclassified,
    IpAddress,
    Domain,
    Uri,
    Ipv6Address,
    DomainFromUri
}

/// <summary>
/// Rechtlicher oder technischer Zugangsstatus.
///
/// Der Zugangsstatus ist unabhängig davon,
/// ob ein Prüfer tatsächlich integriert wurde.
/// </summary>
public enum BlacklistAccessStatus
{
    NotReviewed,
    Permitted,
    RequiresProviderAuthorization,
    Discontinued
}

/// <summary>
/// Technisches Abfrageverfahren.
///
/// DNS kann IP-, Domain- oder URI-bezogene
/// Prüfungen bezeichnen.
/// </summary>
public enum BlacklistQueryMethod
{
    Unclassified,
    Dns,
    HttpApi,
    DataFeed,
    ProviderSpecific
}

/// <summary>
/// Fachlicher Umfang des Reputationsbefunds.
///
/// Das Prüfobjekt und der Bewertungsumfang
/// sind unterschiedliche Eigenschaften.
/// </summary>
public enum BlacklistReputationScope
{
    Unclassified,
    SingleIp,
    IpNetwork,
    AutonomousSystem,
    Domain,
    UriDomain,
    Mixed
}

/// <summary>
/// Stammdaten einer Reputationsquelle.
///
/// Alle zusätzlichen Parameter besitzen
/// rückwärtskompatible Standardwerte.
/// </summary>
public sealed record BlacklistDefinition(
    string Id,
    string DisplayName,
    BlacklistTargetKind TargetKind =
        BlacklistTargetKind.Unclassified,
    BlacklistAccessStatus AccessStatus =
        BlacklistAccessStatus.NotReviewed,
    BlacklistQueryMethod QueryMethod =
        BlacklistQueryMethod.Unclassified,
    BlacklistReputationScope ReputationScope =
        BlacklistReputationScope.Unclassified);

/// <summary>
/// Verbindlicher Referenzkatalog mit 68 Einträgen.
///
/// Eine Katalogdefinition aktiviert keine
/// externen Abfragen.
///
/// Nicht integrierte Quellen dürfen niemals
/// einen fiktiven Negativbefund erhalten.
/// </summary>
public static class BlacklistCatalog
{
    public static IReadOnlyList<BlacklistDefinition> Entries { get; } =
        Array.AsReadOnly(new BlacklistDefinition[]
        {
            // ==========================================
            // URI- und Domain-Reputation
            // ==========================================

            new(
                "ivmuri",
                "ivmURI",
                BlacklistTargetKind.Uri,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed),

            new(
                "sem-fresh",
                "SEM FRESH",
                BlacklistTargetKind.Domain,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Domain),

            new(
                "sem-uri",
                "SEM URI",
                BlacklistTargetKind.DomainFromUri,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.UriDomain),

            new(
                "sem-urired",
                "SEM URIRED",
                BlacklistTargetKind.DomainFromUri,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.UriDomain),

            // ==========================================
            // Historische SORBS-Quellen
            // ==========================================

            // SORBS wurde 2024 eingestellt.
            // Die Referenzen bleiben erhalten.

            new(
                "sorbs-rhsbl-badconf",
                "SORBS RHSBL BADCONF",
                BlacklistTargetKind.Domain,
                BlacklistAccessStatus.Discontinued,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Domain),

            new(
                "sorbs-rhsbl-nomail",
                "SORBS RHSBL NOMAIL",
                BlacklistTargetKind.Domain,
                BlacklistAccessStatus.Discontinued,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Domain),

            // ==========================================
            // Spamhaus und SURBL
            // ==========================================

            new(
                "spamhaus-dbl",
                "Spamhaus DBL",
                BlacklistTargetKind.Domain,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Domain),

            new(
                "surbl-multi",
                "SURBL multi",
                BlacklistTargetKind.DomainFromUri,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.UriDomain),

            // ==========================================
            // 0spam – aktiv
            // ==========================================

            new(
                "0spam",
                "0SPAM",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed),

            new(
                "0spam-rbl",
                "0SPAM RBL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Abusix
            // ==========================================

            new(
                "abusix-mail-intelligence-blacklist",
                "Abusix Mail Intelligence Blacklist",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            new(
                "abusix-mail-intelligence-domain-blacklist",
                "Abusix Mail Intelligence Domain Blacklist",
                BlacklistTargetKind.Domain,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Domain),

            new(
                "abusix-mail-intelligence-exploit-list",
                "Abusix Mail Intelligence Exploit list",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Anonmails – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // spam.dnsbl.anonmails.de

            new(
                "anonmails-dnsbl",
                "Anonmails DNSBL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Backscatterer – Sprint 30.2
            // ==========================================

            // DNS-Zone:
            // ips.backscatterer.org
            //
            // Bewertet fehlgeleitete Rückläufer,
            // nicht allgemeinen Spamversand.

            new(
                "backscatterer",
                "BACKSCATTERER",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Barracuda – Sprint 30.2
            // ==========================================

            // DNS-Zone:
            // b.barracudacentral.org
            //
            // Registrierung/Freigabe erforderlich.

            new(
                "barracuda",
                "BARRACUDA",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // blocklist.de – aktiv
            // ==========================================

            new(
                "blocklist-de",
                "BLOCKLIST.DE",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Noch unklassifizierter Eintrag
            // ==========================================

            new("calivent", "CALIVENT"),

            // ==========================================
            // Team Cymru – Sprint 30.2
            // ==========================================

            // DNS-Zone:
            // bogons.cymru.com
            //
            // Bewertet reservierte oder
            // nicht regulär zugewiesene
            // IPv4-Netzbereiche.

            new(
                "cymru-bogons",
                "CYMRU BOGONS",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.IpNetwork),

            // ==========================================
            // Dan's TOR DNSBL – Sprint 30.2
            // ==========================================

            new(
                "dan-tor",
                "DAN TOR",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            new(
                "dan-torexit",
                "DAN TOREXIT",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Noch unklassifizierter Eintrag
            // ==========================================

            new("drmx", "DRMX"),

            // ==========================================
            // DroneBL – Profil vorbereitet
            // ==========================================

            // Noch nicht aktiv.
            // DNS-Auflösung muss geprüft werden.

            new(
                "drone-bl",
                "DRONE BL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Weitere Rechercheeinträge
            // ==========================================

            new("fabelsources", "FABELSOURCES"),

            new("hil", "HIL"),

            new("hil2", "HIL2"),

            // ==========================================
            // HostKarma – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // hostkarma.junkemailfilter.com
            //
            // Rückgabecodes müssen zwischen
            // Blacklist, Whitelist und anderen
            // Reputationsklassen unterscheiden.

            new(
                "hostkarma-black",
                "Hostkarma Black",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Weitere Rechercheeinträge
            // ==========================================

            new("ibm-dns-blacklist", "IBM DNS Blacklist"),

            new("icmforbidden", "ICMFORBIDDEN"),

            // ==========================================
            // IMP SPAM – Sprint 30.4
            // ==========================================

            // Betreiber:
            // ImproWare AG
            //
            // DNS-Zone:
            // spamrbl.imp.ch
            //
            // IP-Adressen, von denen Spam
            // beobachtet wurde.
            //
            // Die Positivkontrolle,
            // Rückgabecodes und Nutzung
            // werden vor Aktivierung geprüft.

            new(
                "imp-spam",
                "IMP SPAM",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // IMP WORM – Sprint 30.4
            // ==========================================

            // Betreiber:
            // ImproWare AG
            //
            // DNS-Zone:
            // wormrbl.imp.ch
            //
            // IP-Adressen von Rechnern,
            // die durch Schadsoftware
            // kompromittiert erscheinen.
            //
            // Ein Treffer ist nicht
            // zwangsläufig Spamversand.

            new(
                "imp-worm",
                "IMP WORM",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // INTERSERVER – Sprint 30.4
            // ==========================================

            // Betreiber:
            // InterServer / MailBaby
            //
            // DNS-Zone:
            // rbl.interserver.net
            //
            // Dokumentierte Positivkontrolle:
            // 2.0.0.127.rbl.interserver.net
            //
            // Eintrag für auffällige IPs,
            // beispielsweise Spam- und
            // Brute-Force-Aktivitäten.
            //
            // Die genaue Rückgabecode-
            // interpretation und Nutzung
            // müssen verifiziert werden.

            new(
                "interserver",
                "INTERSERVER",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // invaluement
            // ==========================================

            new(
                "ivmsip",
                "ivmSIP",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            new(
                "ivmsip24",
                "ivmSIP24",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.IpNetwork),

            // ==========================================
            // Weitere Rechercheeinträge
            // ==========================================

            new("jippg", "JIPPG"),

            new("kemptbl", "KEMPTBL"),

            new("konstant", "Konstant"),

            // ==========================================
            // LASHBACK – Sprint 30.4
            // ==========================================

            // Betreiber:
            // LashBack
            //
            // DNSBL-Zone historisch:
            // ubl.lashback.com
            //
            // Bewertet IPv4-Adressen, von
            // denen E-Mails an eigens für
            // Abmeldelisten erzeugte Adressen
            // gesendet wurden.
            //
            // Dies ist ein Hinweis auf
            // missbräuchliche Verwendung
            // von Suppression-Listen.
            //
            // Die aktuelle DNSBL-Erreichbarkeit
            // und die Bedingungen für unseren
            // Monitoring-Zugriff sind noch
            // gesondert zu prüfen.

            new(
                "lashback",
                "LASHBACK",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Mailspike – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // bl.mailspike.net
            //
            // Kombiniert Reputationswerte
            // und aktuelle Spamwellen.

            new(
                "mailspike-bl",
                "MAILSPIKE BL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // DNS-Zone:
            // z.mailspike.net
            //
            // Zero-Hour-Erkennung.

            new(
                "mailspike-z",
                "MAILSPIKE Z",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // MSRBL Phishing – Sprint 30.4
            // ==========================================

            // Betreiber:
            // MSRBL
            //
            // DNS-Zone:
            // phishing.rbl.msrbl.net
            //
            // IP-Adressen von Hosts,
            // die Phishing-E-Mails versenden.
            //
            // Es gibt Hinweise auf derzeit
            // nicht antwortende DNS-Zonen.
            //
            // Deshalb keine Aktivierung
            // ohne erfolgreiche Kontrollen.

            new(
                "msrbl-phishing",
                "MSRBL Phishing",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // MSRBL Spam – Sprint 30.4
            // ==========================================

            // DNS-Zone:
            // spam.rbl.msrbl.net
            //
            // IP-Adressen von Hosts,
            // die Spam-E-Mails versenden.
            //
            // Prüfumfang und DNS-Erreichbarkeit
            // müssen vor Aktivierung
            // getrennt validiert werden.

            new(
                "msrbl-spam",
                "MSRBL Spam",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Weitere Rechercheeinträge
            // ==========================================

            new("netherrelays", "NETHERRELAYS"),

            new("netherunsure", "NETHERUNSURE"),

            new("nosolicitado", "NoSolicitado"),

            // ==========================================
            // PSBL – aktiv
            // ==========================================

            new(
                "psbl",
                "PSBL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // SpamRATS – Sprint 30.2
            // ==========================================

            // Anbieterzugang/API-Key erforderlich.

            new(
                "rats-dyna",
                "RATS Dyna",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            new(
                "rats-noptr",
                "RATS NoPtr",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            new(
                "rats-spam",
                "RATS Spam",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Weitere Rechercheeinträge
            // ==========================================

            new("rbl-jp", "RBL JP"),

            // ==========================================
            // s5h.net – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // all.s5h.net

            new(
                "s5h-net",
                "s5h.net",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            new("schulte", "SCHULTE"),

            // ==========================================
            // Spam Eating Monkey
            // ==========================================

            new(
                "sem-backscatter",
                "SEM BACKSCATTER",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            new(
                "sem-black",
                "SEM BLACK",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Weitere Rechercheeinträge
            // ==========================================

            new(
                "sender-score-reputation-network",
                "Sender Score Reputation Network"),

            new("servicesnet", "SERVICESNET"),

            // ==========================================
            // SpamCop
            // ==========================================

            new(
                "spamcop",
                "SPAMCOP",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Spamhaus ZEN
            // ==========================================

            new(
                "spamhaus-zen",
                "Spamhaus ZEN",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed),

            // ==========================================
            // Suomispam – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // bl.suomispam.net
            //
            // Unterschiedliche Rückgabecodes
            // für Einzel-IP, Netzbereiche
            // und andere Auffälligkeiten.

            new(
                "suomispam-reputation",
                "Suomispam Reputation",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed),

            // ==========================================
            // SWINOG – Sprint 30.4
            // ==========================================

            // Betreiber:
            // ImproWare AG / SwiNOG
            //
            // DNS-Zone:
            // dnsrbl.swinog.ch
            //
            // IPv4-Reputationsliste aus
            // Spamtrap-Beobachtungen.
            //
            // Nicht verwechseln mit der
            // separaten domainbasierten
            // Zone uribl.swinog.ch.
            //
            // Zugangsbedingungen und
            // positive Testantworten
            // werden vor Aktivierung geprüft.

            new(
                "swinog",
                "SWINOG",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Weitere Rechercheeinträge
            // ==========================================

            new("triumf", "TRIUMF"),

            new("truncate", "TRUNCATE"),

            // ==========================================
            // UCEPROTECT – aktiv
            // ==========================================

            new(
                "uceprotectl1",
                "UCEPROTECTL1",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            new(
                "uceprotectl2",
                "UCEPROTECTL2",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.IpNetwork),

            new(
                "uceprotectl3",
                "UCEPROTECTL3",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.AutonomousSystem),

            // ==========================================
            // Verbleibende Rechercheeinträge
            // ==========================================

            new(
                "woodys-smtp-blacklist",
                "Woodys SMTP Blacklist"),

            new("zapbl", "ZapBL"),

            // ==========================================
            // SPFBL – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // dnsbl.spfbl.net
            //
            // Unterschiedliche Antwortcodes
            // für Spam und andere Risiken.

            new(
                "spfbl-dnsbl",
                "SPFBL DNSBL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed)
        });
}
