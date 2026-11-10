
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
            //
            // Prüft insbesondere Domains und
            // einzelne IP-Adressen aus Links.
            //
            // DNSBL-basierter Anbieterzugang
            // mit gesonderter Freigabe.
            //
            // Die konkrete URL-Normalisierung
            // wird später implementiert.

            new(
                "ivmuri",
                "ivmURI",
                BlacklistTargetKind.Uri,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed),

            // SEM FRESH:
            // Reputation kürzlich registrierter
            // Domains, nicht der Versand-IP.

            new(
                "sem-fresh",
                "SEM FRESH",
                BlacklistTargetKind.Domain,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Domain),

            // SEM URI:
            // Aus Nachrichten-URLs extrahierte
            // Domains beziehungsweise Hostnamen.

            new(
                "sem-uri",
                "SEM URI",
                BlacklistTargetKind.DomainFromUri,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.UriDomain),

            // SEM URIRED:
            // Erweiterte URI-Reputationsliste.

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

            // SORBS wurde im Juni 2024 eingestellt.
            //
            // Die historischen Prüfverfahren
            // waren domainbasierte DNS-Abfragen.
            //
            // Diese Einträge bleiben erhalten,
            // dürfen jedoch keine fiktiven
            // negativen Ergebnisse liefern.

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

            // Spamhaus DBL:
            // Ausschließlich Domain-Reputation.
            // IPv4-Adressen dürfen nicht als
            // Domain-Lookups abgefragt werden.
            //
            // Kommerzieller Zugriff über
            // geeigneten Spamhaus-Zugang.

            new(
                "spamhaus-dbl",
                "Spamhaus DBL",
                BlacklistTargetKind.Domain,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Domain),

            // SURBL Multi:
            // URI-bezogene Domain-Reputation.
            // Die Antwort enthält codierte
            // Informationen zu Teillisten.
            //
            // Für die spätere Implementierung
            // sind die Rückgabebits auszuwerten.

            new(
                "surbl-multi",
                "SURBL multi",
                BlacklistTargetKind.DomainFromUri,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.UriDomain),

            // ==========================================
            // Bereits integrierte 0spam-Listen
            // ==========================================

            // 0spam:
            // IPv4-basierte DNS-Abfrage.
            // Antwortcodes können zwischen
            // Einzel-IP- und Netzbewertungen
            // unterscheiden.

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

            // Abusix unterscheidet IP-basierte
            // und domainbasierte Datensätze.
            //
            // Die konkrete Zugangsberechtigung
            // bleibt vor einer Aktivierung
            // gesondert zu klären.

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
            // Noch nicht verifizierte Anbieter
            // ==========================================

            new("anonmails-dnsbl", "Anonmails DNSBL"),

            new("backscatterer", "BACKSCATTERER"),

            new("barracuda", "BARRACUDA"),

            // ==========================================
            // blocklist.de – bereits integriert
            // ==========================================

            // IPv4-Reputation mit verschiedenen
            // Angriffsarten als Listungsgrund.
            //
            // Ein Treffer bedeutet nicht zwingend,
            // dass Spam verschickt wurde.

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

            new("cymru-bogons", "CYMRU BOGONS"),

            new("dan-tor", "DAN TOR"),

            new("dan-torexit", "DAN TOREXIT"),

            new("drmx", "DRMX"),

            // ==========================================
            // DroneBL
            // ==========================================

            // IPv4-DNSBL.
            //
            // Die lokale DNS-Auflösung
            // muss vor der Aktivierung
            // erneut geprüft werden.
            //
            // Das Profil ist bereits angelegt,
            // aber nicht im Monitoring aktiv.

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

            // ivmSIP:
            // Einzelne IPv4-Adressen.

            new(
                "ivmsip",
                "ivmSIP",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ivmSIP24:
            // Netzbezogene IP-Reputation
            // auf /24-Basis.

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
            // Weitere Rechercheeinträge
            // ==========================================

            new("rats-dyna", "RATS Dyna"),

            new("rats-noptr", "RATS NoPtr"),

            new("rats-spam", "RATS Spam"),

            new("rbl-jp", "RBL JP"),

            new("s5h-net", "s5h.net"),

            new("schulte", "SCHULTE"),

            // ==========================================
            // Spam Eating Monkey – IP-Listen
            // ==========================================

            // SEM BACKSCATTER:
            // IPv4-Adressen mit Backscatter-
            // Aktivitäten gegenüber Spamtraps.

            new(
                "sem-backscatter",
                "SEM BACKSCATTER",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // SEM BLACK:
            // IPv4-Adressen aus Spamtrap-
            // oder Policy-Erkenntnissen.

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

            // IPv4-basierte DNSBL.
            //
            // Technisches Anbieterprofil
            // wurde bereits geprüft.
            //
            // Freigabe für das konkrete
            // kommerzielle Monitoring
            // noch nicht abgeschlossen.

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

            // IP-basierte DNS-Reputation.
            //
            // Bei kommerziellem Zugriff
            // wird ein geeigneter DQS- oder
            // vergleichbarer Vertrag benötigt.
            //
            // Die Rückgabecodes müssen
            // anbieterspezifisch interpretiert
            // werden.

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
            // UCEPROTECT – alle Level integriert
            // ==========================================

            // Level 1:
            // Einzelne IPv4-Adresse.

            new(
                "uceprotectl1",
                "UCEPROTECTL1",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // Level 2:
            // Größerer IPv4-Netzbereich.

            new(
                "uceprotectl2",
                "UCEPROTECTL2",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.IpNetwork),

            // Level 3:
            // Reputation des zugehörigen ASN.
            //
            // Die Abfrage verwendet weiterhin
            // die IPv4-Adresse als Eingabe.

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
            // Eine eigene technische
            // Klassifizierung steht noch aus.

            new("spfbl-dnsbl", "SPFBL DNSBL")
        });
}