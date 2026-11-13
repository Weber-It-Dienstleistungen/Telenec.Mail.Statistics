
using Telenec.Mail.Statistics.Admin.Models;

namespace Telenec.Mail.Statistics.Admin.Services;

/// <summary>
/// Gemeinsame Schnittstelle für
/// Administrationsabfragen des Statistikservers.
///
/// Unterstützt die bestehende Nutzungsstatistik
/// und die Mailserver-Reputation.
///
/// Blacklist-Prüfungen werden ausschließlich
/// auf ausdrückliche Anforderung gestartet.
/// </summary>
public interface IAdminStatisticsClient
{
    /// <summary>
    /// Ruft die bestehende Nutzungsstatistik ab.
    ///
    /// API:
    /// GET /api/v1/admin/dashboard
    /// </summary>
    Task<DashboardStatisticsResponse> GetDashboardStatisticsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ruft den letzten serverseitigen
    /// Blacklist-Monitoring-Zustand ab.
    ///
    /// API:
    /// GET /api/v1/admin/reputation
    ///
    /// Es wird keine neue Blacklist-Prüfung
    /// ausgelöst.
    /// </summary>
    Task<ReputationMonitoringResponse> GetReputationAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Startet ausdrücklich eine neue
    /// serverseitige Blacklist-Prüfung.
    ///
    /// API:
    /// POST /api/v1/admin/reputation/check
    ///
    /// Die Methode wartet auf den Abschluss
    /// der serverseitigen Prüfoperation.
    ///
    /// Erfolgreicher Abschluss:
    /// HTTP 204 No Content.
    ///
    /// Das tatsächliche Prüfergebnis muss
    /// anschließend über GetReputationAsync
    /// abgerufen werden.
    ///
    /// Es werden keine DNSBL-Abfragen
    /// durch den WPF-Client ausgeführt.
    /// </summary>
    Task TriggerReputationCheckAsync(
        CancellationToken cancellationToken = default);
}
