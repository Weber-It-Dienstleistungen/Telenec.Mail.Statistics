
namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Technisches Prüfobjekt einer Reputationsquelle.
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
/// Zugangsberechtigung für die vorgesehene Abfrage.
/// Dieser Wert sagt nichts über die aktuelle
/// Erreichbarkeit des Anbieters aus.
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
/// Die optionalen Parameter sichern die
/// Kompatibilität mit bisherigen Aufrufen.
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
/// Referenzkatalog mit exakt 68 MXToolbox-Einträgen.
///
/// Die Reihenfolge und Kennungen sind Bestandteil
/// unserer Regressionstests.
///
/// Die Katalogdefinition führt keine DNS-Abfrage
/// aus und aktiviert keinen Monitoring-Anbieter.
///
/// Ein nicht ausgeführter Check darf niemals
/// als negatives Prüfergebnis erscheinen.
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
            // Die Referenzen bleiben dokumentiert.

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
            // Anbieterregistrierung erforderlich.

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

            new(
                "blocklist-de",
                "BLOCKLIST.DE",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // CALIVENT – Sprint 30.5
            // ==========================================

            // Betreiber:
            // CALIVENT / DNSBL Perú
            //
            // DNS-Zone:
            // dnsbl.calivent.com.pe
            //
            // Öffentlich dokumentierter IPv4-DNSBL.
            //
            // Der Betreiber bezeichnet den Dienst
            // als kostenlosen Unternehmensdienst.
            //
            // Dokumentierter Listungscode:
            // 127.0.0.2
            //
            // Die Nutzungsbedingungen für das
            // regelmäßige externe Monitoring
            // werden noch gesondert bewertet.
            //
            // Der veröffentlichte Beispiel-Lookup
            // ersetzt keine echte Positivkontrolle.

            new(
                "calivent",
                "CALIVENT",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Team Cymru – Sprint 30.2
            // ==========================================

            // DNS-Zone:
            // bogons.cymru.com
            //
            // Bewertet IP-Netzbereiche,
            // keine allgemeine Spam-Reputation.

            new(
                "cymru-bogons",
                "CYMRU BOGONS",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.IpNetwork),

            // ==========================================
            // Dan's TOR – Sprint 30.2
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
            // DrMX – Sprint 30.5
            // ==========================================

            // Historische und dokumentierte DNS-Zone:
            // bl.drmx.org
            //
            // IP-basierte Liste bekannter Spamquellen.
            //
            // Externe DNSBL-Verzeichnisse führen
            // die Quelle weiterhin.
            //
            // Die positive RFC-5782-Kontrolladresse
            // lieferte bei externen Prüfungen
            // zuletzt keine zuverlässige Antwort.
            //
            // Deshalb:
            // Technisch klassifiziert,
            // aber nicht für das Monitoring
            // freigegeben oder aktiviert.

            new(
                "drmx",
                "DRMX",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // DroneBL – Profil vorbereitet
            // ==========================================

            // DNS-Zone:
            // dnsbl.dronebl.org
            //
            // Noch nicht aktiv.
            // DNS-Kontrollen müssen geprüft werden.

            new(
                "drone-bl",
                "DRONE BL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Fabel Spamsources – Sprint 30.5
            // ==========================================

            // Betreiber:
            // Fabel, Dänemark
            //
            // DNS-Zone:
            // spamsources.fabel.dk
            //
            // Der Betreiber dokumentiert
            // ausschließlich IP-basierte Abfragen.
            //
            // Neben einzelnen Spamquellen können
            // auch größere Netzbereiche gelistet
            // werden, beispielsweise wenn
            // Missbrauchsbeschwerden unbeantwortet
            // bleiben.
            //
            // Daher:
            // TargetKind = IpAddress
            // ReputationScope = Mixed
            //
            // Der Betreiber beschreibt die
            // DNSBL als kostenlosen Dienst
            // ohne Gewährleistung.
            //
            // Die konkreten Rückgabecodes und
            // Kontrolladressen werden erst vor
            // der tatsächlichen Aktivierung
            // implementiert und getestet.

            new(
                "fabelsources",
                "FABELSOURCES",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed),

            // ==========================================
            // Habeas Infringer List – Sprint 30.6
            // ==========================================

            // Historische DNS-Zone:
            // hil.habeas.com
            //
            // IP-basierte Habeas Infringer List.
            //
            // Historisch zur Erkennung von
            // Mailservern verwendet, die gegen
            // die Habeas-Richtlinien verstießen.
            //
            // Die Quelle wird als zugangs-
            // beschränkte DNSBL geführt.
            //
            // Die aktuelle Berechtigung und
            // die DNS-Erreichbarkeit müssen
            // vor jeder Aktivierung geprüft werden.
            //
            // HIL und HIL2 bleiben eigenständige
            // historische Referenzkennungen.

            new(
                "hil",
                "HIL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // Habeas HIL2 – Sprint 30.6
            // ==========================================

            // Historische IPv4-Reputationsquelle
            // aus dem Habeas-Umfeld.
            //
            // MXToolbox dokumentiert HIL2
            // separat von HIL.
            //
            // Die konkrete historische DNS-Zone
            // konnte nicht unabhängig und
            // zuverlässig bestätigt werden.
            //
            // Die DNS-Einordnung beruht auf
            // der historischen DNSBL-Verwendung.
            //
            // Eine technische Gleichsetzung
            // mit hil.habeas.com wäre unzulässig.
            //
            // Zugangsrecht und Erreichbarkeit
            // bleiben ausdrücklich ungeprüft.
            //
            // Keine automatische Aktivierung.

            new(
                "hil2",
                "HIL2",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // HostKarma – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // hostkarma.junkemailfilter.com
            //
            // Wichtig:
            // 127.0.0.1 = Whitelist
            // 127.0.0.2 = Blacklist
            //
            // DNS-Antwort bedeutet nicht
            // automatisch Blacklist-Treffer.

            new(
                "hostkarma-black",
                "Hostkarma Black",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // IBM DNS Blacklist – Sprint 30.6
            // ==========================================

            // Historische DNS-Zone:
            // dnsbl.cobion.com
            //
            // IP-basierte Spam-Reputationsquelle.
            //
            // Historisch Bestandteil der
            // IBM Proventia Mail Security
            // und Lotus Protector for
            // Mail Security Produkte.
            //
            // Eine externe öffentliche
            // Nutzungsfreigabe ergibt sich
            // aus der Produktintegration nicht.
            //
            // Deshalb bleibt die Berechtigung
            // für unser Monitoring ungeprüft.
            //
            // Keine automatische Aktivierung.

            new(
                "ibm-dns-blacklist",
                "IBM DNS Blacklist",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // ICMFORBIDDEN – Sprint 30.6
            // ==========================================

            // Historische DNS-Zone:
            // forbidden.icm.edu.pl
            //
            // IPv4-basierte Reputationsquelle
            // aus dem ICM-Umfeld.
            //
            // Die historische Beschreibung
            // bezieht sich insbesondere
            // auf offene Mail-Relays.
            //
            // Eine erfolgreiche DNS-Antwort
            // oder Nutzungsfreigabe für unser
            // Monitoring ist nicht bestätigt.
            //
            // Anbieterprofil erst nach
            // kontrollierter DNS-Prüfung.

            new(
                "icmforbidden",
                "ICMFORBIDDEN",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // IMP – Sprint 30.4
            // ==========================================

            // Historische DNS-Zone:
            // spamrbl.imp.ch

            new(
                "imp-spam",
                "IMP SPAM",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // Historische DNS-Zone:
            // wormrbl.imp.ch

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

            // DNS-Zone:
            // rbl.interserver.net
            //
            // Aktivierung erst nach erfolgreicher
            // positiver und negativer DNS-Kontrolle.

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
            // JIPPG – Sprint 30.6
            // ==========================================

            // DNS-Zone:
            // mail-abuse.blacklist.jippg.org
            //
            // Japanisches Relay Blackhole
            // List Project.
            //
            // IPv4-basierte DNSBL.
            //
            // Die Quelle berücksichtigt
            // auch Informationen anderer
            // Reputationslisten.
            //
            // Historisch dokumentierter
            // Listungscode:
            // 127.0.0.2
            //
            // Externe Verzeichnisse haben
            // noch 2026 Listungen beobachtet.
            //
            // Dies allein bestätigt weder
            // dauerhafte Erreichbarkeit noch
            // Nutzungsfreigabe.
            //
            // Vor einer Aktivierung müssen
            // Kontrollabfragen erfolgen.

            new(
                "jippg",
                "JIPPG",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // KEMPTBL – Sprint 30.6
            // ==========================================

            // DNS-Zone:
            // dnsbl.kempt.net
            //
            // Kempt.net DNS Black List.
            //
            // Erkennt auffällige Mailserver
            // anhand empfangener Spam-Nachrichten.
            //
            // Die historische MXToolbox-
            // Beschreibung erwähnt auch
            // mögliche Subnetzlistungen.
            //
            // Daher wird der fachliche Umfang
            // vorläufig als Mixed eingeordnet.
            //
            // Eine aktuelle Bestätigung der
            // Netzlistungspraxis steht noch aus.
            //
            // Die Freigabe für regelmäßiges
            // externes Monitoring ist ungeprüft.

            new(
                "kemptbl",
                "KEMPTBL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed),

            // ==========================================
            // Konstant – Sprint 30.6
            // ==========================================

            // Historische DNS-Zone:
            // bl.konstant.no
            //
            // Norwegische IPv4-DNSBL.
            //
            // Historisch basierend auf
            // Spamfallen und gemeldeten
            // Spamquellen.
            //
            // Externe DNS-Prüfungen zeigen
            // Hinweise auf fehlende Antworten.
            //
            // Daraus wird ohne Bestätigung
            // des Betreibers noch kein
            // formeller Discontinued-Status.
            //
            // Keine automatische Aktivierung.

            new(
                "konstant",
                "Konstant",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // LASHBACK – Sprint 30.4
            // ==========================================

            // Historische DNSBL:
            // ubl.lashback.com
            //
            // Prüft den Missbrauch von
            // Abmeldeadressen.
            //
            // Aktuelle DNS-Erreichbarkeit
            // noch zu verifizieren.

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
            // Kombinierte Reputation.

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
            // Zero-Hour-Spamaktivität.

            new(
                "mailspike-z",
                "MAILSPIKE Z",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // MSRBL – Sprint 30.4
            // ==========================================

            // Historische DNS-Zone:
            // phishing.rbl.msrbl.net
            //
            // Aktuelle Funktion noch ungeklärt.

            new(
                "msrbl-phishing",
                "MSRBL Phishing",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // Historische DNS-Zone:
            // spam.rbl.msrbl.net

            new(
                "msrbl-spam",
                "MSRBL Spam",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // NETHERRELAYS – Sprint 30.6
            // ==========================================

            // Historische DNS-Zone:
            // relays.nether.net
            //
            // IPv4-basierte Open-Relay-Liste.
            //
            // Der Betreiber untersuchte
            // auffällige Mailserver auf
            // unerlaubte Weiterleitung.
            //
            // Die DNS-Zone wird in externen
            // Verzeichnissen dokumentiert.
            //
            // Aktuelle Erreichbarkeit und
            // Nutzungsbedingungen sind
            // nicht abschließend bestätigt.
            //
            // Keine automatische Aktivierung.

            new(
                "netherrelays",
                "NETHERRELAYS",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // NETHERUNSURE – Sprint 30.6
            // ==========================================

            // Historische DNS-Zone:
            // unsure.nether.net
            //
            // Zweite Reputationskategorie
            // des historischen Nether.net-
            // Open-Relay-Projekts.
            //
            // Nicht mit der bestätigten
            // Relay-Liste gleichzusetzen.
            //
            // Insbesondere dürfen Treffer
            // nicht ungeprüft als identische
            // Befunde interpretiert werden.
            //
            // Aktuelle DNS-Erreichbarkeit
            // und Zugangsrechte ungeprüft.

            new(
                "netherunsure",
                "NETHERUNSURE",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // NoSolicitado – Sprint 30.6
            // ==========================================

            // DNS-Zone:
            // bl.nosolicitado.org
            //
            // Spanischsprachige IPv4-DNSBL
            // gegen unerwünschte E-Mails.
            //
            // Verschiedene Verzeichnisse
            // widersprechen sich hinsichtlich
            // des aktuellen Betriebsstatus.
            //
            // Allerdings wurden im Oktober
            // 2026 erfolgreiche Kontrollen
            // und tatsächliche Listungen
            // dokumentiert.
            //
            // Daher ausdrücklich nicht
            // als Discontinued klassifiziert.
            //
            // Historisch dokumentierter
            // Listungscode:
            // 127.0.0.2
            //
            // Vor einer Integration werden
            // aktuelle DNS-Kontrollen und
            // Nutzungsbedingungen geprüft.

            new(
                "nosolicitado",
                "NoSolicitado",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

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

            // Konfigurierbarer Anbieter-API-Key
            // erforderlich.
            //
            // Keine Zugangsdaten im Quellcode.

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
            // RBL JP – Sprint 30.6
            // ==========================================

            // Historische japanische DNSBL-Familie.
            //
            // Dokumentierte IPv4-Zonen:
            // all.rbl.jp
            // short.rbl.jp
            // virus.rbl.jp
            //
            // Zusätzlich existierten getrennte
            // domainbezogene Listen.
            //
            // Der MXToolbox-Referenzeintrag
            // wird historisch als IP-basierte
            // Reputationsprüfung geführt.
            //
            // Die genaue Zuordnung einer
            // einzelnen Zone zur MXToolbox-
            // Referenz ist nicht abschließend
            // unabhängig bestätigt.
            //
            // Deshalb wird keine Zone als
            // verbindliches Anbieterprofil
            // aktiviert.
            //
            // Aktueller Betrieb und
            // Nutzungsfreigabe ungeprüft.

            new(
                "rbl-jp",
                "RBL JP",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

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

            // ==========================================
            // SCHULTE – Sprint 30.6
            // ==========================================

            // DNS-Zone:
            // rbl.schulte.org
            //
            // Betreibergeführte IPv4-DNSBL.
            //
            // Der Betreiber dokumentiert
            // ausdrücklich die öffentliche
            // Verwendung per DNS-Abfrage.
            //
            // Neben Einzeladressen werden
            // unter bestimmten Bedingungen
            // größere Netzbereiche wie
            // /24 und /16 berücksichtigt.
            //
            // Daher:
            // TargetKind = IpAddress
            // ReputationScope = Mixed
            //
            // Die dokumentierte Erlaubnis
            // ersetzt keine Prüfung der
            // aktuellen DNS-Erreichbarkeit.
            //
            // Vor einer Aktivierung müssen
            // Kontrolladressen und Rückgabecodes
            // verifiziert werden.

            new(
                "schulte",
                "SCHULTE",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed),

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
            // Sender Score – Sprint 30.6
            // ==========================================

            // Betreiber:
            // Validity / ehemaliges Return Path.
            //
            // DNS-Zone:
            // bl.score.senderscore.com
            //
            // IP-basierte Blocklist.
            //
            // Nicht mit dem separaten
            // numerischen Sender Score
            // von 0 bis 100 verwechseln.
            //
            // Seit 2024 dokumentiert
            // Validity Beschränkungen bei
            // mehr als 10000 DNS-Abfragen
            // innerhalb von 30 Tagen.
            //
            // Eine Anbieterregistrierung
            // kann erforderlich werden.
            //
            // Vor Implementierung müssen
            // die aktuellen Vertrags- und
            // Nutzungsbedingungen geprüft
            // und die erforderlichen
            // Berechtigungen geklärt werden.
            //
            // Unbekannte oder abweisende
            // DNS-Rückgabecodes dürfen
            // nicht als Listung oder
            // Nichtlistung interpretiert werden.
            //
            // Keine automatische Aktivierung.

            new(
                "sender-score-reputation-network",
                "Sender Score Reputation Network",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // SERVICESNET – Sprint 30.6
            // ==========================================

            // Historische DNS-Zone:
            // korea.services.net
            //
            // South Korean Network Blocking List.
            //
            // Bewertet IP-Netzbereiche,
            // insbesondere geografisch
            // zugeordnete koreanische Netze.
            //
            // Berücksichtigt außerdem
            // bestimmte ältere Netzbereiche
            // mit dokumentierter Spamproblematik.
            //
            // Es handelt sich nicht um eine
            // klassische Einzel-IP-Liste.
            //
            // Daher:
            // TargetKind = IpAddress
            // ReputationScope = IpNetwork
            //
            // Ein Treffer weist nicht zwingend
            // auf aktuellen Spamversand hin.
            //
            // Eine automatische Bewertung
            // als kompromittierter Mailserver
            // wäre fachlich falsch.
            //
            // Nutzungsbedingungen und
            // DNS-Kontrollen bleiben offen.

            new(
                "servicesnet",
                "SERVICESNET",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.IpNetwork),

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

            // DNS-Zone:
            // dnsrbl.swinog.ch

            new(
                "swinog",
                "SWINOG",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // TRIUMF – Sprint 30.6
            // ==========================================

            // Historische DNS-Zone:
            // rbl2.triumf.ca
            //
            // IPv4-basierte DNSBL aus dem
            // TRIUMF-Mailserverumfeld.
            //
            // Dokumentierte Rückgabecodes:
            //
            // 127.0.0.2
            // Auffällige SpamAssassin-Bewertung
            //
            // 127.0.0.3
            // Spamfallen-Treffer
            //
            // 127.0.0.4
            // Meldung durch TRIUMF-Benutzer
            //
            // 127.0.0.5
            // Manuelle Aufnahme
            //
            // Die Codes stammen aus historischer
            // Dokumentation und müssen vor einer
            // Integration erneut geprüft werden.
            //
            // Externe Prüfungen zeigten zuletzt
            // keine erfolgreiche Positivkontrolle.
            //
            // Keine Aussage über eine formelle
            // Einstellung des Dienstes.
            //
            // Keine automatische Aktivierung.

            new(
                "triumf",
                "TRIUMF",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // GBUdb TRUNCATE – Sprint 30.5
            // ==========================================

            // Betreiber:
            // GBUdb / ARM Research Labs
            //
            // DNS-Zone:
            // truncate.gbudb.net
            //
            // RFC-5782-kompatible IPv4-DNSBL.
            //
            // Dokumentierte Kontrolladressen:
            // 127.0.0.2 = positiver Testeintrag
            // 127.0.0.1 = negativer Testeintrag
            //
            // Listungscode:
            // 127.0.0.2
            //
            // Berücksichtigt IP-Adressen,
            // die nach den Kriterien des
            // GBUdb-Netzwerks besonders
            // auffällige E-Mails versenden.
            //
            // Aktuelle externe Verzeichnisse
            // zeigen erfolgreiche Testabfragen.
            //
            // Die konkreten Nutzungsbedingungen
            // für unser Monitoring müssen noch
            // abschließend geprüft werden.

            new(
                "truncate",
                "TRUNCATE",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

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
            // Woody's SMTP Blacklist – Sprint 30.5
            // ==========================================

            // DNS-Zone:
            // blacklist.woody.ch
            //
            // Klassische IPv4-Reputationsliste.
            //
            // Zusätzlich existierten separate
            // IPv6- und URI-bezogene Zonen.
            // Diese sind nicht Bestandteil
            // dieses konkreten Referenzeintrags.
            //
            // Bei öffentlichen Prüfungen
            // lieferte die positive
            // RFC-5782-Kontrolladresse
            // zuletzt kein gültiges Ergebnis.
            //
            // Der Anbieter wird deshalb
            // vorerst nicht aktiviert.
            //
            // Technische Klassifizierung und
            // aktuelle Funktionsfähigkeit
            // müssen getrennt betrachtet werden.

            new(
                "woodys-smtp-blacklist",
                "Woodys SMTP Blacklist",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.SingleIp),

            // ==========================================
            // ZapBL – Sprint 30.5
            // ==========================================

            // Betreiber:
            // ZapBL
            //
            // DNS-Zone:
            // dnsbl.zapbl.net
            //
            // IPv4-basierte DNSBL.
            //
            // Dokumentierte Kontrolladressen:
            // 127.0.0.2 = gelisteter Testeintrag
            // 127.0.0.1 = negativer Testeintrag
            //
            // Dokumentierter Listungscode:
            // 127.0.0.2
            //
            // Der Betreiber erlaubt die
            // kostenlose Nutzung unabhängig
            // von Organisationsgröße und
            // Abfragemenge.
            //
            // Bei sehr großen Abfragemengen
            // wird ein lokaler Mirror empfohlen.
            //
            // Die Listungspolitik erlaubt
            // auch Eskalationen auf größere
            // IP-Netzbereiche.
            //
            // Daher:
            // TargetKind = IpAddress
            // ReputationScope = Mixed
            //
            // Separate RHSBL-Zone:
            // rhsbl.zapbl.net
            //
            // Diese gehört nicht zum
            // hier betrachteten IP-Eintrag.
            //
            // Auch die erlaubte Quelle wird
            // erst nach DNS- und Regressionstests
            // im Monitoring aktiviert.

            new(
                "zapbl",
                "ZapBL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed),

            // ==========================================
            // SPFBL – Sprint 30.3
            // ==========================================

            // DNS-Zone:
            // dnsbl.spfbl.net
            //
            // Unterschiedliche Antworten
            // für Spam und sonstige Risiken.
            //
            // Keine direkte SMTP-Ablehnung
            // durch unser Monitoring.

            new(
                "spfbl-dnsbl",
                "SPFBL DNSBL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed,
                BlacklistQueryMethod.Dns,
                BlacklistReputationScope.Mixed)
        });
}
