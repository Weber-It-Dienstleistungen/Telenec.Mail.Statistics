
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
/// Dieser Status ist unabhängig von der
/// tatsächlichen Monitoring-Integration.
/// </summary>
public enum BlacklistAccessStatus
{
    NotReviewed,
    Permitted,
    RequiresProviderAuthorization,
    Discontinued
}

/// <summary>
/// Technisches Verfahren zur Reputationsabfrage.
///
/// Dns bezeichnet auch DNSBL, RHSBL und URIBL.
/// Ein DNS-Eintrag ist nicht zwangsläufig mit
/// dem bestehenden IPv4-Checker kompatibel.
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
/// Bei einer IPv4-DNS-Abfrage kann sich
/// der Befund auf die einzelne Adresse,
/// einen Netzbereich oder ein ASN beziehen.
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
/// Neue Metadaten bleiben optional, damit
/// ältere Konstruktoraufrufe gültig bleiben.
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
/// Nur tatsächliche Monitoring-Ergebnisse
/// dürfen als integrierte Prüfungen gelten.
///
/// Eine unbekannte oder eingestellte Quelle
/// darf niemals einen fiktiven Negativbefund
/// erhalten.
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
            // Eingestellte SORBS-Quellen
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
            // 0spam – bereits aktiv
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
            //
            // IP-basierte Spam-Reputationsliste.
            //
            // Der Betreiber dokumentiert eine
            // SpamAssassin-Integration per DNSBL.
            //
            // Die konkreten Antwortcodes und
            // Nutzungsbedingungen müssen vor
            // produktiver Aktivierung geprüft werden.

            new(
                "anonmails-dnsbl",
                "Anonmails DNSBL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // BACKSCATTERER – Sprint 30.2
            // ==========================================

            // DNS-Zone:
            // ips.backscatterer.org
            //
            // Bewertet Backscatter-Aktivitäten,
            // nicht allgemeine Spam-Reputation.

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
            // Vor Aktivierung Registrierung
            // beziehungsweise Freigabe klären.

            new(
                "barracuda",
                "BARRACUDA",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // blocklist.de – bereits aktiv
            // ==========================================

            // Enthält mehrere Angriffskategorien.
            // Eine Listung beweist nicht
            // automatisch aktiven Spamversand.

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
            // Prüft die Zugehörigkeit zu
            // reservierten IP-Bereichen.
            //
            // Kein Spam-Nachweis.

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

            // DNS-Zone:
            // tor.dan.me.uk
            //
            // Erkennung von Tor-Knoten.
            // Ein Treffer ist kein Spam-Nachweis.

            new(
                "dan-tor",
                "DAN TOR",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // DNS-Zone:
            // torexit.dan.me.uk
            //
            // Erkennung von Tor-Exit-Knoten.

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
            // DroneBL – Profil bereits vorbereitet
            // ==========================================

            // DNS-Zone:
            // dnsbl.dronebl.org
            //
            // Noch nicht aktiv.
            // Lokale DNS-Auflösung muss
            // zunächst geprüft werden.

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
            // HostKarma Black – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // hostkarma.junkemailfilter.com
            //
            // Der Dienst liefert unterschiedliche
            // Reputationsklassen für IP-Adressen.
            //
            // Dokumentierte Antwortcodes:
            //
            // 127.0.0.1 = Whitelist
            // 127.0.0.2 = Blacklist
            // 127.0.0.3 = Yellowlist
            // 127.0.0.4 = Brownlist
            // 127.0.0.5 = NOBL
            //
            // Weitere A-Records enthalten
            // ergänzende Anbieterinformationen.
            //
            // WICHTIG:
            //
            // Für den Referenzeintrag
            // "Hostkarma Black" ist nur
            // 127.0.0.2 ein Blacklist-Treffer.
            //
            // Die übrigen Antwortcodes dürfen
            // nicht pauschal als Listed gelten.
            //
            // Der generische DNSBL-Checker
            // benötigt dafür ein passend
            // konfiguriertes Profil.

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

            new("imp-spam", "IMP SPAM"),

            new("imp-worm", "IMP WORM"),

            new("interserver", "INTERSERVER"),

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

            new("lashback", "LASHBACK"),

            // ==========================================
            // Mailspike BL – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // bl.mailspike.net
            //
            // Kombinierte IP-Reputationsliste.
            //
            // Enthält aktuelle Spamwellen aus
            // z.mailspike.net sowie besonders
            // schlechte Reputationswerte aus
            // rep.mailspike.net.
            //
            // Dokumentierte Treffer-Codes:
            //
            // 127.0.0.2  = Zero-Hour-Ereignis
            // 127.0.0.10 = Reputation L5
            // 127.0.0.11 = Reputation L4
            // 127.0.0.12 = Reputation L3
            //
            // Diese Codes müssen getrennt
            // interpretiert werden.
            //
            // Öffentliche DNS-Abfragen sind
            // an Nutzungsgrenzen gebunden.
            //
            // Freigabe noch nicht abgeschlossen.

            new(
                "mailspike-bl",
                "MAILSPIKE BL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Mailspike Z – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // z.mailspike.net
            //
            // Erkennt IP-Adressen, die an
            // aktuellen verteilten Spamwellen
            // beteiligt sind.
            //
            // Dokumentierter Listungscode:
            // 127.0.0.2
            //
            // Die Liste unterscheidet sich
            // fachlich vom kombinierten
            // Mailspike-BL-Eintrag.
            //
            // Beide Referenzen bleiben
            // separat erhalten.

            new(
                "mailspike-z",
                "MAILSPIKE Z",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Weitere Rechercheeinträge
            // ==========================================

            new("msrbl-phishing", "MSRBL Phishing"),

            new("msrbl-spam", "MSRBL Spam"),

            new("netherrelays", "NETHERRELAYS"),

            new("netherunsure", "NETHERUNSURE"),

            new("nosolicitado", "NoSolicitado"),

            // ==========================================
            // PSBL – bereits aktiv
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

            // DNS-Zonen mit anbieterspezifischem
            // API-Key:
            //
            // [API_KEY].dyna.spamrats.com
            // [API_KEY].noptr.spamrats.com
            // [API_KEY].spam.spamrats.com
            //
            // Erwartete Rückgabecodes:
            //
            // Dyna:  127.0.0.36
            // NoPtr: 127.0.0.37
            // Spam:  127.0.0.38
            //
            // Zugangsdaten dürfen nicht
            // im Quellcode hinterlegt werden.

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
            //
            // Aggregierte DNSBL für IP-Adressen,
            // von denen Spam oder andere
            // unerwünschte Aktivitäten
            // beobachtet wurden.
            //
            // Der Dienst dokumentiert auch
            // IPv6-Unterstützung.
            //
            // Das aktuelle Monitoring prüft
            // weiterhin ausschließlich IPv4.
            //
            // Kontrolladressen und Antwortcodes
            // sind vor Aktivierung zu prüfen.

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

            // Profil bereits vorbereitet.
            // Kommerzielle Nutzung noch
            // nicht abschließend freigegeben.

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
            // Suomispam Reputation – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // bl.suomispam.net
            //
            // Reputation von IP-Adressen,
            // vor allem im Zusammenhang
            // mit finnischsprachigem Spam.
            //
            // Der Betreiber nennt folgende
            // Listungsgründe:
            //
            // 127.0.0.2  = Spamquelle
            // 127.0.0.3  = Eskalation/Netzbereich
            // 127.0.0.4  = Zielseite von Spam
            // 127.0.0.5  = Verdächtige Aktivität
            // 127.0.0.99 = Sonstiger Grund
            //
            // Deshalb verwenden wir
            // ReputationScope.Mixed.
            //
            // Die Quelle ist laut Betreiber
            // für gewöhnliche leichte Nutzung
            // als DNSBL verfügbar.
            //
            // Ob unser kommerzielles Monitoring
            // darunter fällt, bleibt zu prüfen.

            new(
                "suomispam-reputation",
                "Suomispam Reputation",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed),

            // ==========================================
            // Weitere Rechercheeinträge
            // ==========================================

            new("swinog", "SWINOG"),

            new("triumf", "TRIUMF"),

            new("truncate", "TRUNCATE"),

            // ==========================================
            // UCEPROTECT – bereits aktiv
            // ==========================================

            // Level 1: Einzel-IP
            // Level 2: Netzbereich
            // Level 3: ASN

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
            // SPFBL DNSBL – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // dnsbl.spfbl.net
            //
            // IP-basierte DNSBL mit
            // unterschiedlichen Bewertungsarten.
            //
            // Dokumentierte Antworten:
            //
            // NXDOMAIN:
            // Keine gemeldete Auffälligkeit.
            //
            // 127.0.0.2:
            // Schlechte Reputation/Spam.
            //
            // 127.0.0.3:
            // Verantwortlichkeit oder
            // RFC-Konformität problematisch.
            //
            // 127.0.0.4:
            // Kein identifizierter Maildienst,
            // NAT oder Privatanschluss.
            //
            // 127.0.0.5:
            // Unzuverlässige Behandlung
            // von Missbrauchsmeldungen.
            //
            // DNS REFUSED:
            // Kann auf überschrittene
            // Abfragelimits hinweisen.
            //
            // WICHTIG:
            //
            // Nicht jeder Antwortcode ist
            // ein Nachweis für Spamversand.
            //
            // Der Betreiber untersagt die
            // Nutzung dieser Liste zur
            // direkten SMTP-Ablehnung.
            //
            // Unser Anwendungsfall ist
            // ausschließlich Monitoring.
            //
            // Auch IPv6 wird vom Anbieter
            // unterstützt, jedoch noch
            // nicht von unserem Checker.
            //
            // Zugangs- und Nutzungsbedingungen
            // bleiben vor Aktivierung zu prüfen.

            new(
                "spfbl-dnsbl",
                "SPFBL DNSBL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed)
        });
}
