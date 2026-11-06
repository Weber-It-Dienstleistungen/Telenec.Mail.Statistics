
using Telenec.Mail.Statistics.Admin.Models;

namespace Telenec.Mail.Statistics.Admin.Services;

/// <summary>
/// Gemeinsame Schnittstelle für lesende
/// Administrationsabfragen des Statistikservers.
///
/// Unterstützt die bestehende Nutzungsstatistik
/// und die neue Mailserver-Reputation.
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
}
