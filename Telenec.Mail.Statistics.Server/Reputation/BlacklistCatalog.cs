
namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Art des Eingabeobjekts einer Reputationsprüfung.
///
/// Unclassified:
/// Technische Eingabeart noch nicht verifiziert.
///
/// IpAddress:
/// IPv4-Adresse für einen klassischen DNSBL-Lookup.
///
/// Domain:
/// Domainname als direktes Prüfobjekt.
///
/// Uri:
/// URI-bezogene Prüfung mit anbieterspezifischer
/// Extraktion oder Normalisierung.
///
/// Ipv6Address:
/// IPv6-Adresse; benötigt gegebenenfalls eine
/// andere DNSBL-Kodierung als IPv4.
///
/// DomainFromUri:
/// Domain beziehungsweise Hostname, der aus
/// einer URL oder URI extrahiert wird.
///
/// Ein URI-bezogener Anbieter muss deshalb
/// nicht zwingend die vollständige URL prüfen.
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
/// Zugangsstatus einer Reputationsquelle.
///
/// NotReviewed:
/// Die zulässige Nutzung muss geprüft werden.
///
/// Permitted:
/// Die Nutzung wurde für die bisher
/// vorgesehene Abfrageart freigegeben.
///
/// RequiresProviderAuthorization:
/// Eine geeignete Anbieterfreigabe bzw.
/// ein vertraglicher Zugang ist erforderlich.
///
/// Discontinued:
/// Die ursprüngliche Quelle wurde eingestellt.
///
/// Die Angabe ist unabhängig davon, ob
/// tatsächlich ein Prüfer registriert wurde.
/// </summary>
public enum BlacklistAccessStatus
{
    NotReviewed,
    Permitted,
    RequiresProviderAuthorization,
    Discontinued
}

/// <summary>
/// Technischer Weg zum Abrufen der Reputation.
///
/// Unclassified:
/// Das Verfahren ist noch nicht verifiziert.
///
/// Dns:
/// Klassische DNSBL-, RHSBL- oder URIBL-Abfrage.
///
/// HttpApi:
/// Anbieter stellt eine HTTP-basierte API bereit.
///
/// DataFeed:
/// Anbieter liefert herunterladbare Daten,
/// die lokal ausgewertet werden müssen.
///
/// ProviderSpecific:
/// Ein anderes anbieterspezifisches Verfahren.
///
/// Die Angabe Dns bedeutet nicht automatisch,
/// dass unser vorhandener IPv4-Checker
/// verwendet werden kann.
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
/// Fachlicher Bewertungsumfang.
///
/// Dieser ist ausdrücklich unabhängig von
/// der Art des technischen Prüfobjekts.
///
/// Beispielsweise wird bei UCEPROTECT Level 3
/// eine IPv4-Adresse abgefragt, das Ergebnis
/// betrifft jedoch die ASN-Reputation.
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
/// Zentrale Stammdaten einer Reputationsquelle.
///
/// TargetKind:
/// Welches Objekt muss vorbereitet werden?
///
/// AccessStatus:
/// Unter welchen Bedingungen darf die
/// Quelle verwendet werden?
///
/// QueryMethod:
/// Über welches technische Verfahren
/// erfolgt die Abfrage?
///
/// ReputationScope:
/// Worauf bezieht sich das Ergebnis?
///
/// Die neuen Parameter sind optional.
/// Bestehende Aufrufe bleiben kompatibel.
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
/// Referenzkatalog der 68 MXToolbox-Einträge.
///
/// Alle 68 Einträge bleiben Bestandteil
/// des verbindlichen Entwicklungsumfangs.
///
/// Eine noch nicht untersuchte Quelle
/// bleibt als Unclassified gekennzeichnet.
///
/// Eine abgeschaltete Quelle bleibt ebenfalls
/// Bestandteil des Referenzkatalogs,
/// darf aber nicht als erfolgreich
/// geprüft ausgegeben werden.
///
/// Der Katalog selbst führt keine Abfragen aus.
/// Eine aktive Integration wird ausschließlich
/// durch den Monitoring-Dienst bestimmt.
/// </summary>
public static class BlacklistCatalog
{
    public static IReadOnlyList<BlacklistDefinition> Entries { get; } =
        Array.AsReadOnly(new BlacklistDefinition[]
        {
            // ==========================================
            // URI- und Domain-Reputationslisten
            // ==========================================

            // invaluement ivmURI:
            // URI-bezogene DNS-Reputation mit
            // anbieterspezifischer Normalisierung.

            new(
                "ivmuri",
                "ivmURI",
                BlacklistTargetKind.Uri,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed),

            // SEM FRESH:
            // Reputation kürzlich registrierter Domains.

            new(
                "sem-fresh",
                "SEM FRESH",
                BlacklistTargetKind.Domain,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Domain),

            // SEM URI und URIRED:
            // Domains beziehungsweise Hostnamen,
            // die aus Nachrichten-URLs stammen.

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
            // Historische SORBS-Listen
            // ==========================================

            // Der Dienst wurde 2024 eingestellt.
            // Beide Referenzen bleiben erhalten,
            // dürfen aber keinen fiktiven
            // Negativbefund liefern.

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
            // 0spam – bereits integriert
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
            // Noch nicht klassifizierte Quelle
            // ==========================================

            new("anonmails-dnsbl", "Anonmails DNSBL"),

            // ==========================================
            // BACKSCATTERER – Sprint 30.2
            // ==========================================

            // DNSBL-Zone: ips.backscatterer.org
            //
            // Prüft IPv4-Adressen auf Backscatter,
            // beispielsweise fehlgeleitete
            // Unzustellbarkeitsmeldungen.
            //
            // Keine allgemeine Spamversenderliste.
            //
            // Für unsere reine Monitoring-Anwendung
            // muss die Nutzungsfreigabe noch
            // abschließend bewertet werden.

            new(
                "backscatterer",
                "BACKSCATTERER",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // BARRACUDA – Sprint 30.2
            // ==========================================

            // DNSBL-Zone: b.barracudacentral.org
            //
            // IP-Reputation für Mailversand.
            //
            // Vor einer produktiven Verwendung
            // muss die erforderliche Registrierung
            // beziehungsweise Zugangsberechtigung
            // des DNS-Resolvers geklärt werden.

            new(
                "barracuda",
                "BARRACUDA",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // blocklist.de – bereits integriert
            // ==========================================

            // Verschiedene Angriffskategorien.
            // Ein Treffer ist kein automatischer
            // Beweis für Spamversand.

            new(
                "blocklist-de",
                "BLOCKLIST.DE",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Weitere Rechercheeinträge
            // ==========================================

            new("calivent", "CALIVENT"),

            // ==========================================
            // CYMRU BOGONS – Sprint 30.2
            // ==========================================

            // DNS-Zone: bogons.cymru.com
            //
            // IPv4-Adresse als Abfrageobjekt.
            //
            // Die Listung bezieht sich auf
            // reservierte oder nicht regulär
            // zugewiesene IPv4-Adressbereiche.
            //
            // Sie ist keine Spam-Reputation.
            //
            // Team Cymru dokumentiert zusätzlich
            // Fullbogon- und IPv6-Datenquellen.
            // Diese sind nicht automatisch Teil
            // des hier referenzierten Eintrags.

            new(
                "cymru-bogons",
                "CYMRU BOGONS",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.IpNetwork),

            // ==========================================
            // DAN TOR – Sprint 30.2
            // ==========================================

            // DNS-Zone: tor.dan.me.uk
            //
            // Erfasst Tor-Knoten allgemein.
            // Ein Treffer ist nicht gleichbedeutend
            // mit Spam oder einer kompromittierten IP.
            //
            // Der Betreiber dokumentiert auch
            // IPv6-Abfragen über Nibble-Reversal.
            //
            // Unser aktueller Prüfgegenstand
            // bleibt zunächst die IPv4-Adresse.

            new(
                "dan-tor",
                "DAN TOR",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // DAN TOREXIT – Sprint 30.2
            // ==========================================

            // DNS-Zone: torexit.dan.me.uk
            //
            // Erfasst ausschließlich Tor-Exit-Knoten.
            //
            // Ein Exit-Knoten ist nicht automatisch
            // als Spamversender einzustufen.

            new(
                "dan-torexit",
                "DAN TOREXIT",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            new("drmx", "DRMX"),

            // ==========================================
            // DroneBL – technisch vorbereitet
            // ==========================================

            // Der lokale Windows-Resolver konnte
            // die positive Kontrolladresse bisher
            // nicht zuverlässig auflösen.
            //
            // Deshalb weiterhin nicht aktiv.

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

            new("hostkarma-black", "Hostkarma Black"),

            new("ibm-dns-blacklist", "IBM DNS Blacklist"),

            new("icmforbidden", "ICMFORBIDDEN"),

            new("imp-spam", "IMP SPAM"),

            new("imp-worm", "IMP WORM"),

            new("interserver", "INTERSERVER"),

            // ==========================================
            // invaluement IP-Listen
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

            new("mailspike-bl", "MAILSPIKE BL"),

            new("mailspike-z", "MAILSPIKE Z"),

            new("msrbl-phishing", "MSRBL Phishing"),

            new("msrbl-spam", "MSRBL Spam"),

            new("netherrelays", "NETHERRELAYS"),

            new("netherunsure", "NETHERUNSURE"),

            new("nosolicitado", "NoSolicitado"),

            // ==========================================
            // PSBL – bereits integriert
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

            // Die folgenden drei Listen verwenden
            // IP-basierte DNSBL-Abfragen.
            //
            // Anbieter-DNS-Zonen:
            //
            // [API_KEY].dyna.spamrats.com
            // [API_KEY].noptr.spamrats.com
            // [API_KEY].spam.spamrats.com
            //
            // Eine geeignete Registrierung und
            // ein API-Key sind erforderlich.
            //
            // Der tatsächliche Schlüssel darf
            // nicht in den Quellcode gelangen.
            //
            // Dokumentierte Rückgabecodes:
            //
            // RATS Dyna:  127.0.0.36
            // RATS NoPtr: 127.0.0.37
            // RATS Spam:  127.0.0.38
            //
            // Weitere Antworten dürfen nicht
            // automatisch als Listung gelten.

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

            new("s5h-net", "s5h.net"),

            new("schulte", "SCHULTE"),

            // ==========================================
            // Spam Eating Monkey – IP-Listen
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

            // Das technische Profil ist vorbereitet.
            // Die Nutzung für unser kommerzielles
            // Monitoring bleibt noch zu klären.

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

            // Anbieterzugang erforderlich.
            // Antwortcodes müssen explizit
            // interpretiert werden.

            new(
                "spamhaus-zen",
                "Spamhaus ZEN",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed),

            // ==========================================
            // Weitere Rechercheeinträge
            // ==========================================

            new(
                "suomispam-reputation",
                "Suomispam Reputation"),

            new("swinog", "SWINOG"),

            new("triumf", "TRIUMF"),

            new("truncate", "TRUNCATE"),

            // ==========================================
            // UCEPROTECT – bereits integriert
            // ==========================================

            // Level 1: Einzel-IP
            // Level 2: IPv4-Netzbereich
            // Level 3: Autonomes System

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

            // Beim ursprünglichen MXToolbox-Test
            // wurde ein Timeout beobachtet.
            //
            // Technische Klassifizierung
            // steht weiterhin aus.

            new("spfbl-dnsbl", "SPFBL DNSBL")
        });
}
