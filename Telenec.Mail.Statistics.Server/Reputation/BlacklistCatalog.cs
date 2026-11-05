
namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Fachliche Kategorie einer Reputationsquelle.
/// Unclassified bedeutet, dass die Abfrageart noch
/// nicht abschließend verifiziert wurde.
/// </summary>
public enum BlacklistTargetKind
{
    Unclassified,
    IpAddress,
    Domain,
    Uri
}

/// <summary>
/// Technischer und rechtlicher Integrationsstatus.
///
/// Wichtig:
/// Dieser Status beschreibt nicht, ob eine IP
/// oder Domain auf einer Blacklist steht.
/// </summary>
public enum BlacklistAccessStatus
{
    NotReviewed,
    Permitted,
    RequiresProviderAuthorization,
    Discontinued
}

/// <summary>
/// Stammdatensatz einer Reputationsquelle.
///
/// Es handelt sich ausschließlich um Metadaten.
/// Ein aktuelles Prüfergebnis wird hier nicht gespeichert.
/// </summary>
public sealed record BlacklistDefinition(
    string Id,
    string DisplayName,
    BlacklistTargetKind TargetKind =
        BlacklistTargetKind.Unclassified,
    BlacklistAccessStatus AccessStatus =
        BlacklistAccessStatus.NotReviewed);

/// <summary>
/// Referenzkatalog der 68 MXToolbox-Einträge
/// aus der Bestandsaufnahme vom 05.11.2026.
///
/// Der Katalog dient als vollständige Soll-Liste.
///
/// Nicht klassifizierte Einträge dürfen nicht
/// automatisch abgefragt werden.
///
/// Die Freigabe einer technischen Abfrage erfolgt
/// erst nach Prüfung der jeweiligen Anbieterbedingungen.
///
/// Der Katalog selbst führt keine DNS- oder
/// HTTP-Anfragen aus.
/// </summary>
public static class BlacklistCatalog
{
    public static IReadOnlyList<BlacklistDefinition> Entries { get; } =
        Array.AsReadOnly(new BlacklistDefinition[]
        {
            new("ivmuri", "ivmURI"),

            new("sem-fresh", "SEM FRESH"),

            new("sem-uri", "SEM URI"),

            new("sem-urired", "SEM URIRED"),

            // SORBS wurde am 05.06.2024 eingestellt.
            // Die historischen Einträge bleiben erhalten,
            // dürfen aber nicht als aktive Prüfungen gelten.

            new(
                "sorbs-rhsbl-badconf",
                "SORBS RHSBL BADCONF",
                BlacklistTargetKind.Domain,
                BlacklistAccessStatus.Discontinued),

            new(
                "sorbs-rhsbl-nomail",
                "SORBS RHSBL NOMAIL",
                BlacklistTargetKind.Domain,
                BlacklistAccessStatus.Discontinued),

            // Spamhaus DBL ist eine Domain-Reputationsliste.
            // Kommerzielle Verwendung benötigt einen
            // geeigneten Zugang des Anbieters.

            new(
                "spamhaus-dbl",
                "Spamhaus DBL",
                BlacklistTargetKind.Domain,
                BlacklistAccessStatus.RequiresProviderAuthorization),

            // SURBL prüft insbesondere Domains,
            // die als Ressourcen in Nachrichten-URIs
            // vorkommen.
            // Für unsere professionelle Einbindung
            // ist eine entsprechende Freigabe erforderlich.

            new(
                "surbl-multi",
                "SURBL multi",
                BlacklistTargetKind.Uri,
                BlacklistAccessStatus.RequiresProviderAuthorization),

            new("0spam", "0SPAM"),

            new("0spam-rbl", "0SPAM RBL"),

            new(
                "abusix-mail-intelligence-blacklist",
                "Abusix Mail Intelligence Blacklist"),

            new(
                "abusix-mail-intelligence-domain-blacklist",
                "Abusix Mail Intelligence Domain Blacklist"),

            new(
                "abusix-mail-intelligence-exploit-list",
                "Abusix Mail Intelligence Exploit list"),

            new("anonmails-dnsbl", "Anonmails DNSBL"),

            new("backscatterer", "BACKSCATTERER"),

            new("barracuda", "BARRACUDA"),

            new("blocklist-de", "BLOCKLIST.DE"),

            new("calivent", "CALIVENT"),

            new("cymru-bogons", "CYMRU BOGONS"),

            new("dan-tor", "DAN TOR"),

            new("dan-torexit", "DAN TOREXIT"),

            new("drmx", "DRMX"),

            new("drone-bl", "DRONE BL"),

            new("fabelsources", "FABELSOURCES"),

            new("hil", "HIL"),

            new("hil2", "HIL2"),

            new("hostkarma-black", "Hostkarma Black"),

            new("ibm-dns-blacklist", "IBM DNS Blacklist"),

            new("icmforbidden", "ICMFORBIDDEN"),

            new("imp-spam", "IMP SPAM"),

            new("imp-worm", "IMP WORM"),

            new("interserver", "INTERSERVER"),

            new("ivmsip", "ivmSIP"),

            new("ivmsip24", "ivmSIP24"),

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

            // PSBL ist eine IP-Reputationsliste.
            // Der Anbieter erlaubt ausdrücklich
            // die freie Verwendung der Liste.

            new(
                "psbl",
                "PSBL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted),

            new("rats-dyna", "RATS Dyna"),

            new("rats-noptr", "RATS NoPtr"),

            new("rats-spam", "RATS Spam"),

            new("rbl-jp", "RBL JP"),

            new("s5h-net", "s5h.net"),

            new("schulte", "SCHULTE"),

            new("sem-backscatter", "SEM BACKSCATTER"),

            new("sem-black", "SEM BLACK"),

            new(
                "sender-score-reputation-network",
                "Sender Score Reputation Network"),

            new("servicesnet", "SERVICESNET"),

            // SpamCop dokumentiert IP-basierte DNSBL-Abfragen.
            // Die kommerzielle Nutzung unseres
            // konkreten Monitorings ist noch zu prüfen.

            new(
                "spamcop",
                "SPAMCOP",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed),

            // Spamhaus ZEN ist eine IP-Reputationsliste.
            // Für Telenecs kommerzielle Verwendung
            // benötigen wir einen geeigneten Zugang.

            new(
                "spamhaus-zen",
                "Spamhaus ZEN",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.RequiresProviderAuthorization),

            new(
                "suomispam-reputation",
                "Suomispam Reputation"),

            new("swinog", "SWINOG"),

            new("triumf", "TRIUMF"),

            new("truncate", "TRUNCATE"),

            new("uceprotectl1", "UCEPROTECTL1"),

            new("uceprotectl2", "UCEPROTECTL2"),

            new("uceprotectl3", "UCEPROTECTL3"),

            new(
                "woodys-smtp-blacklist",
                "Woodys SMTP Blacklist"),

            new("zapbl", "ZapBL"),

            // MXToolbox meldete für diese Liste
            // bei der Bestandsaufnahme einen Timeout.
            // Das ist kein Beleg für eine Listung
            // und auch kein Freigabenachweis.

            new("spfbl-dnsbl", "SPFBL DNSBL")
        });
}
