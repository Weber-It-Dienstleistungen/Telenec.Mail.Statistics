
namespace Telenec.Mail.Statistics.Admin.Models;

/// <summary>
/// Datenmodell für die Antwort des geschützten
/// Reputation-Endpunkts.
///
/// API:
/// GET /api/v1/admin/reputation
///
/// Dieses Modell enthält den aktuellen
/// serverseitigen Monitoring-Snapshot.
///
/// Die WPF-Anwendung führt selbst keine
/// Blacklist-Abfragen aus.
/// </summary>
public sealed record ReputationMonitoringResponse
{
    /// <summary>
    /// Überwachter Mailserver-Hostname.
    /// Beispiel: mail.necnet.de
    /// </summary>
    public string Hostname { get; init; } = string.Empty;

    /// <summary>
    /// Erwartete öffentliche Versand-IP.
    /// </summary>
    public string ExpectedIpv4 { get; init; } = string.Empty;

    /// <summary>
    /// Zuletzt über DNS ermittelte IPv4-Adressen.
    /// </summary>
    public IReadOnlyList<string> ResolvedIpv4Addresses
    {
        get;
        init;
    } = Array.Empty<string>();

    /// <summary>
    /// Zeitpunkt des letzten Prüfversuchs in UTC.
    ///
    /// Null bedeutet, dass noch keine
    /// Prüfung stattgefunden hat.
    /// </summary>
    public DateTimeOffset? LastAttemptUtc { get; init; }

    /// <summary>
    /// Zeitpunkt der letzten erfolgreich
    /// interpretierten Blacklist-Prüfung.
    ///
    /// Fehlgeschlagene Prüfungen verändern
    /// diesen Wert nicht.
    /// </summary>
    public DateTimeOffset? LastSuccessfulCheckUtc
    {
        get;
        init;
    }

    /// <summary>
    /// Ergebnis des aktuellen PSBL-Prüflaufs.
    ///
    /// In späteren Ausbaustufen wird dieses
    /// Einzelmodell durch eine Sammlung von
    /// Prüfergebnissen ergänzt.
    /// </summary>
    public ReputationCheckResponse Result
    {
        get;
        init;
    } = new();
}

/// <summary>
/// Ergebnis einer einzelnen Blacklist-Prüfung.
///
/// Das Modell verwendet bewusst Zeichenfolgen
/// für den Status, damit der Client nicht von
/// numerischen Enum-Werten des Servers abhängt.
/// </summary>
public sealed record ReputationCheckResponse
{
    /// <summary>
    /// Kennung der Prüfliste.
    /// Beispiel: psbl
    /// </summary>
    public string BlacklistId { get; init; } = string.Empty;

    /// <summary>
    /// Tatsächlich geprüftes Ziel.
    /// Beispiel: 80.79.237.70
    /// </summary>
    public string CheckedTarget { get; init; } = string.Empty;

    /// <summary>
    /// Fachlicher Status als Zeichenfolge.
    ///
    /// Aktuell mögliche Werte:
    ///
    /// NotChecked
    /// NotListed
    /// Listed
    /// Failed
    /// AccessDenied
    /// Unknown
    ///
    /// Unbekannte zukünftige Statuswerte
    /// können als Zeichenfolge eingelesen
    /// und später gesondert behandelt werden.
    /// </summary>
    public string Status { get; init; } = "NotChecked";

    /// <summary>
    /// Beginn der Prüfung in UTC.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; init; }

    /// <summary>
    /// Abschluss der Prüfung in UTC.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>
    /// Technische Antwort des Anbieters.
    ///
    /// Beispiel: NXDOMAIN
    /// </summary>
    public string? ProviderResponse { get; init; }

    /// <summary>
    /// Optionale Fehlermeldung bei einer
    /// nicht erfolgreich abgeschlossenen Prüfung.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gibt an, ob das Prüfergebnis
    /// fachlich eindeutig ist.
    /// </summary>
    public bool IsConclusive { get; init; }

    /// <summary>
    /// Tatsächlicher Listungszustand.
    ///
    /// true  = gelistet
    /// false = nicht gelistet
    /// null  = keine verlässliche Aussage
    ///
    /// Null darf niemals als False
    /// interpretiert werden.
    /// </summary>
    public bool? IsListed { get; init; }
}
