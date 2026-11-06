
namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Fachliche Kategorie einer Reputationsquelle.
/// </summary>
public enum BlacklistTargetKind
{
    Unclassified,
    IpAddress,
    Domain,
    Uri
}

/// <summary>
/// Technischer und rechtlicher Zugangsstatus.
///
/// Dieser Status sagt nicht aus, ob eine
/// Quelle bereits im Monitoring integriert ist.
///
/// Die tatsächliche Integration ergibt sich
/// ausschließlich aus registrierten Prüfergebnissen.
/// </summary>
public enum BlacklistAccessStatus
{
    NotReviewed,
    Permitted,
    RequiresProviderAuthorization,
    Discontinued
}

/// <summary>
/// Stammdaten einer Reputationsquelle.
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
/// Der Katalog selbst führt keine Abfragen aus.
///
/// Eine Quelle wird nur dann als integriert
/// angezeigt, wenn der Monitoring-Dienst
/// tatsächlich ein Prüfergebnis liefert.
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

            // Historische SORBS-Einträge.
            // Der Betrieb wurde 2024 eingestellt.

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

            // Kommerzielle Nutzung nur mit
            // entsprechendem Anbieterzugang.

            new(
                "spamhaus-dbl",
                "Spamhaus DBL",
                BlacklistTargetKind.Domain,
                BlacklistAccessStatus.RequiresProviderAuthorization),

            new(
                "surbl-multi",
                "SURBL multi",
                BlacklistTargetKind.Uri,
                BlacklistAccessStatus.RequiresProviderAuthorization),

            // Erste aktive DNSBL-Anbietergruppe.
            // Kommerzielle DNSBL-Nutzung erlaubt.

            new(
                "0spam",
                "0SPAM",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted),

            new(
                "0spam-rbl",
                "0SPAM RBL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted),

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

            // DroneBL:
            // Kommerzielle Nutzung erlaubt.
            //
            // Technisch vorbereitet, derzeit
            // aber NICHT im Monitoring registriert.
            //
            // Die lokale Windows-DNS-Auflösung
            // muss vor einer Aktivierung geklärt werden.

            new(
                "drone-bl",
                "DRONE BL",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted),

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

            // Bisheriger produktiver PSBL-Prüfer.

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

            // SpamCop:
            // IP-DNSBL ist dokumentiert.
            // Nutzungsfreigabe für Telenec
            // noch nicht abschließend geprüft.

            new(
                "spamcop",
                "SPAMCOP",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.NotReviewed),

            // Spamhaus ZEN:
            // Geeigneter kommerzieller Zugang erforderlich.

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

            // UCEPROTECT:
            // Kostenlose private und kommerzielle
            // Nutzung laut Betreiber erlaubt.
            //
            // Level 1 ist bereits integriert.
            // Level 2 und 3 bleiben getrennte,
            // noch nicht aktivierte Quellen.

            new(
                "uceprotectl1",
                "UCEPROTECTL1",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted),

            new(
                "uceprotectl2",
                "UCEPROTECTL2",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted),

            new(
                "uceprotectl3",
                "UCEPROTECTL3",
                BlacklistTargetKind.IpAddress,
                BlacklistAccessStatus.Permitted),

            new(
                "woodys-smtp-blacklist",
                "Woodys SMTP Blacklist"),

            new("zapbl", "ZapBL"),

            // MXToolbox meldete für diese Quelle
            // bei der ursprünglichen Bestandsaufnahme
            // einen Timeout.
            //
            // Es liegt noch kein belastbarer
            // eigener Prüfstatus vor.

            new("spfbl-dnsbl", "SPFBL DNSBL")
        });
}
