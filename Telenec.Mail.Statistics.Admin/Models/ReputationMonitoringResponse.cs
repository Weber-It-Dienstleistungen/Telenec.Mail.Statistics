
namespace Telenec.Mail.Statistics.Admin.Models;

/// <summary>
/// Antwort des geschützten Reputation-Endpunkts.
///
/// API:
/// GET /api/v1/admin/reputation
///
/// Enthält den aktuellen Monitoring-Zustand
/// sowie optional die vollständige
/// Blacklist-Übersicht.
///
/// Die WPF-Anwendung führt selbst keine
/// externen Blacklist-Prüfungen aus.
/// </summary>
public sealed record ReputationMonitoringResponse
{
    /// <summary>
    /// Überwachter Mailserver.
    /// </summary>
    public string Hostname { get; init; } = string.Empty;

    /// <summary>
    /// Erwartete öffentliche Versand-IP.
    /// </summary>
    public string ExpectedIpv4 { get; init; } = string.Empty;

    /// <summary>
    /// Zuletzt ermittelte IPv4-Adressen.
    /// </summary>
    public IReadOnlyList<string> ResolvedIpv4Addresses
    {
        get;
        init;
    } = Array.Empty<string>();

    /// <summary>
    /// Letzter Prüfversuch in UTC.
    /// </summary>
    public DateTimeOffset? LastAttemptUtc { get; init; }

    /// <summary>
    /// Letzte erfolgreiche Prüfung in UTC.
    /// </summary>
    public DateTimeOffset? LastSuccessfulCheckUtc
    {
        get;
        init;
    }

    /// <summary>
    /// Bisheriges Einzelprüfergebnis.
    ///
    /// Bleibt aus Gründen der
    /// Rückwärtskompatibilität erhalten.
    ///
    /// Aktuell enthält es das PSBL-Ergebnis.
    /// </summary>
    public ReputationCheckResponse Result
    {
        get;
        init;
    } = new();

    /// <summary>
    /// Neue dynamische Blacklist-Übersicht.
    ///
    /// Null bedeutet, dass der Server
    /// diese Erweiterung noch nicht anbietet.
    ///
    /// Dadurch bleiben ältere Serverversionen
    /// mit dem WPF-Client kompatibel.
    /// </summary>
    public ReputationOverviewResponse? Overview
    {
        get;
        init;
    }
}

/// <summary>
/// Bestehendes Einzelprüfergebnis.
///
/// Dieses Modell wird weiterhin für
/// die bisherige PSBL-Anzeige verwendet.
/// </summary>
public sealed record ReputationCheckResponse
{
    public string BlacklistId { get; init; } =
        string.Empty;

    public string CheckedTarget { get; init; } =
        string.Empty;

    /// <summary>
    /// Fachlicher Prüfstatus als Zeichenfolge.
    ///
    /// NotChecked
    /// NotListed
    /// Listed
    /// Failed
    /// AccessDenied
    /// Unknown
    /// </summary>
    public string Status { get; init; } =
        "NotChecked";

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? ProviderResponse { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsConclusive { get; init; }

    /// <summary>
    /// true  = gelistet
    /// false = nicht gelistet
    /// null  = unbekannt
    /// </summary>
    public bool? IsListed { get; init; }
}

/// <summary>
/// Vollständige Übersicht aller
/// vom Statistikserver gelieferten
/// Blacklist-Referenzeinträge.
///
/// Die Anzahl der Listen wird ausschließlich
/// vom Server bestimmt.
///
/// Der Client verwendet keine eigene,
/// fest hinterlegte Blacklist-Soll-Liste.
/// </summary>
public sealed record ReputationOverviewResponse
{
    /// <summary>
    /// Gesamtzahl der Referenzlisten.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Anzahl der technisch integrierten Listen.
    ///
    /// Eine integrierte Liste muss noch kein
    /// erfolgreiches Prüfergebnis besitzen.
    /// </summary>
    public int IntegratedCount { get; init; }

    /// <summary>
    /// Anzahl der nicht integrierten Listen.
    /// </summary>
    public int NotIntegratedCount { get; init; }

    /// <summary>
    /// Integriert, aber noch nicht geprüft.
    /// </summary>
    public int NotCheckedCount { get; init; }

    /// <summary>
    /// Erfolgreich geprüft und nicht gelistet.
    /// </summary>
    public int NotListedCount { get; init; }

    /// <summary>
    /// Erfolgreich geprüft und gelistet.
    /// </summary>
    public int ListedCount { get; init; }

    /// <summary>
    /// Fehlgeschlagene oder nicht eindeutig
    /// interpretierbare Prüfungen.
    /// </summary>
    public int WarningCount { get; init; }

    /// <summary>
    /// Dynamische Liste aller Einträge.
    ///
    /// Die Reihenfolge wird vom
    /// Statistikserver vorgegeben.
    /// </summary>
    public IReadOnlyList<ReputationBlacklistEntryResponse> Entries
    {
        get;
        init;
    } = Array.Empty<ReputationBlacklistEntryResponse>();
}

/// <summary>
/// Ein einzelner Eintrag der dynamischen
/// Blacklist-Übersicht.
///
/// Enthält sowohl die Stammdaten der
/// Reputationsquelle als auch das
/// gegebenenfalls vorhandene Prüfergebnis.
/// </summary>
public sealed record ReputationBlacklistEntryResponse
{
    /// <summary>
    /// Eindeutige Katalogkennung.
    /// Beispiel: psbl
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Anzeigename in der Oberfläche.
    /// Beispiel: PSBL
    /// </summary>
    public string DisplayName { get; init; } =
        string.Empty;

    /// <summary>
    /// Art des Prüfgegenstands.
    ///
    /// Unclassified
    /// IpAddress
    /// Domain
    /// Uri
    /// </summary>
    public string TargetKind { get; init; } =
        "Unclassified";

    /// <summary>
    /// Integrations- und Zugriffsstatus.
    ///
    /// NotReviewed
    /// Permitted
    /// RequiresProviderAuthorization
    /// Discontinued
    ///
    /// Dies ist kein Blacklist-Prüfergebnis.
    /// </summary>
    public string AccessStatus { get; init; } =
        "NotReviewed";

    /// <summary>
    /// Fachlicher Status.
    ///
    /// NotIntegrated
    /// NotChecked
    /// NotListed
    /// Listed
    /// Failed
    /// AccessDenied
    /// Unknown
    ///
    /// Die spätere Oberfläche verwendet
    /// diesen Wert zur Statusdarstellung.
    /// </summary>
    public string Status { get; init; } =
        "NotIntegrated";

    /// <summary>
    /// Tatsächlich geprüftes Ziel.
    ///
    /// Null bei nicht integrierten Einträgen.
    /// </summary>
    public string? CheckedTarget { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? ProviderResponse { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsConclusive { get; init; }

    /// <summary>
    /// true  = gelistet
    /// false = nicht gelistet
    /// null  = kein verlässliches Ergebnis
    ///
    /// Null darf nicht als "nicht gelistet"
    /// dargestellt werden.
    /// </summary>
    public bool? IsListed { get; init; }
}
