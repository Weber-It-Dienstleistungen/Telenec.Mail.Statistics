
namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Fachliches Ergebnis einer einzelnen Blacklist-Prüfung.
///
/// Wichtig:
/// Eine fehlgeschlagene oder nicht autorisierte Prüfung
/// darf niemals als "Nicht gelistet" bewertet werden.
/// </summary>
public enum BlacklistCheckStatus
{
    /// <summary>
    /// Für diese Liste liegt noch kein Prüfergebnis vor.
    /// </summary>
    NotChecked,

    /// <summary>
    /// Die Abfrage wurde erfolgreich durchgeführt.
    /// Das geprüfte Ziel ist nicht gelistet.
    /// </summary>
    NotListed,

    /// <summary>
    /// Die Abfrage wurde erfolgreich durchgeführt.
    /// Das geprüfte Ziel ist gelistet.
    /// </summary>
    Listed,

    /// <summary>
    /// Die Prüfung ist technisch fehlgeschlagen,
    /// beispielsweise durch einen DNS-Timeout.
    /// </summary>
    Failed,

    /// <summary>
    /// Der Anbieter hat den Zugriff verweigert
    /// oder eine fehlende Autorisierung gemeldet.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// Eine Antwort liegt vor, konnte aber nicht
    /// zuverlässig interpretiert werden.
    /// </summary>
    Unknown
}

/// <summary>
/// Ergebnis einer einzelnen Abfrage gegen eine
/// Reputationsquelle.
///
/// Dieses Modell ist unabhängig vom konkreten
/// Anbieter und kann später für DNSBL- und
/// API-basierte Prüfungen verwendet werden.
/// </summary>
public sealed record BlacklistCheckResult
{
    /// <summary>
    /// Eindeutige Kennung aus dem BlacklistCatalog.
    /// </summary>
    public required string BlacklistId { get; init; }

    /// <summary>
    /// Tatsächlich geprüftes Ziel.
    ///
    /// Beispielsweise:
    /// 80.79.237.70
    /// oder necnet.de
    /// </summary>
    public required string CheckedTarget { get; init; }

    /// <summary>
    /// Fachlicher Prüfstatus.
    /// </summary>
    public required BlacklistCheckStatus Status { get; init; }

    /// <summary>
    /// Beginn des letzten Prüfversuchs in UTC.
    ///
    /// Null bedeutet, dass noch kein
    /// Prüfversuch stattgefunden hat.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; init; }

    /// <summary>
    /// Abschluss des letzten Prüfversuchs in UTC.
    ///
    /// Auch eine fehlgeschlagene Prüfung kann
    /// einen Abschlusszeitpunkt besitzen.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>
    /// Originalantwort des Anbieters, soweit
    /// technisch verfügbar.
    ///
    /// Bei DNSBLs beispielsweise eine
    /// zurückgelieferte DNS-Antwort.
    /// </summary>
    public string? ProviderResponse { get; init; }

    /// <summary>
    /// Optionale technische Fehlermeldung.
    ///
    /// Darf nicht mit dem eigentlichen
    /// Blacklist-Status verwechselt werden.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Nur eine erfolgreich interpretierte
    /// positive oder negative Prüfung
    /// liefert ein belastbares Ergebnis.
    /// </summary>
    public bool IsConclusive =>
        Status is BlacklistCheckStatus.NotListed
            or BlacklistCheckStatus.Listed;

    /// <summary>
    /// Gibt an, ob die geprüfte Adresse
    /// beziehungsweise Domain gelistet ist.
    ///
    /// Null bedeutet ausdrücklich:
    /// Der Listungszustand ist nicht bekannt.
    /// </summary>
    public bool? IsListed =>
        Status switch
        {
            BlacklistCheckStatus.Listed => true,
            BlacklistCheckStatus.NotListed => false,
            _ => null
        };
}
