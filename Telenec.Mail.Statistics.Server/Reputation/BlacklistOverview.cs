
namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Ein Eintrag der Blacklist-Übersicht.
///
/// Metadaten stammen aus dem BlacklistCatalog.
/// Prüfergebnisse stammen ausschließlich aus
/// tatsächlich registrierten Monitoring-Ergebnissen.
///
/// Ein nicht integrierter Anbieter wird niemals
/// als erfolgreich geprüft dargestellt.
/// </summary>
public sealed record BlacklistOverviewEntry(
    string Id,
    string DisplayName,
    string TargetKind,
    string AccessStatus,
    string Status,
    string? CheckedTarget,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ProviderResponse,
    string? ErrorMessage,
    bool IsConclusive,
    bool? IsListed);

/// <summary>
/// Vollständige Blacklist-Übersicht.
///
/// Die Kennzahlen werden aus den Einträgen
/// berechnet und sind nicht fest auf eine
/// bestimmte Anzahl von Anbietern eingestellt.
/// </summary>
public sealed record BlacklistOverview(
    IReadOnlyList<BlacklistOverviewEntry> Entries)
{
    public int TotalCount =>
        Entries.Count;

    public int IntegratedCount =>
        Entries.Count(entry =>
            entry.Status != "NotIntegrated");

    public int NotIntegratedCount =>
        Entries.Count(entry =>
            entry.Status == "NotIntegrated");

    public int NotCheckedCount =>
        Entries.Count(entry =>
            entry.Status == "NotChecked");

    public int NotListedCount =>
        Entries.Count(entry =>
            entry.Status == "NotListed"
            && entry.IsConclusive
            && entry.IsListed == false);

    public int ListedCount =>
        Entries.Count(entry =>
            entry.Status == "Listed"
            && entry.IsConclusive
            && entry.IsListed == true);

    public int WarningCount =>
        Entries.Count(entry =>
            entry.Status is
                "Failed"
                or "AccessDenied"
                or "Unknown");
}

/// <summary>
/// Erstellt eine vollständige Übersicht
/// aus dem bestehenden Referenzkatalog und
/// den verfügbaren Monitoring-Ergebnissen.
///
/// Die Implementierung ist unabhängig von
/// der Anzahl der tatsächlich angebundenen
/// Blacklist-Prüfer.
///
/// Aktuell ist ausschließlich PSBL integriert.
/// Weitere Prüfer können später ihre Ergebnisse
/// über dieselbe Schnittstelle bereitstellen.
/// </summary>
public static class BlacklistOverviewFactory
{
    public static BlacklistOverview Create(
        IReadOnlyDictionary<string, BlacklistCheckResult> latestResults)
    {
        ArgumentNullException.ThrowIfNull(
            latestResults);

        var entries =
            new BlacklistOverviewEntry[
                BlacklistCatalog.Entries.Count];

        for (var index = 0;
             index < BlacklistCatalog.Entries.Count;
             index++)
        {
            var definition =
                BlacklistCatalog.Entries[index];

            /*
             * Nur ein tatsächlich vorhandenes
             * Prüfergebnis gilt als Integration.
             *
             * Ohne Ergebnis bleibt der Katalogeintrag
             * ausdrücklich "NotIntegrated".
             */
            latestResults.TryGetValue(
                definition.Id,
                out var result);

            if (result is null)
            {
                entries[index] =
                    new BlacklistOverviewEntry(
                        Id: definition.Id,
                        DisplayName: definition.DisplayName,
                        TargetKind: definition.TargetKind.ToString(),
                        AccessStatus: definition.AccessStatus.ToString(),
                        Status: "NotIntegrated",
                        CheckedTarget: null,
                        StartedAtUtc: null,
                        CompletedAtUtc: null,
                        ProviderResponse: null,
                        ErrorMessage: null,
                        IsConclusive: false,
                        IsListed: null);

                continue;
            }

            /*
             * Die Statusinformation wird aus dem
             * fachlichen Prüfergebnis übernommen.
             *
             * Insbesondere bleiben Fehler und
             * unbekannte Ergebnisse von einem
             * eindeutigen Negativbefund getrennt.
             */
            entries[index] =
                new BlacklistOverviewEntry(
                    Id: definition.Id,
                    DisplayName: definition.DisplayName,
                    TargetKind: definition.TargetKind.ToString(),
                    AccessStatus: definition.AccessStatus.ToString(),
                    Status: result.Status.ToString(),
                    CheckedTarget: result.CheckedTarget,
                    StartedAtUtc: result.StartedAtUtc,
                    CompletedAtUtc: result.CompletedAtUtc,
                    ProviderResponse: result.ProviderResponse,
                    ErrorMessage: result.ErrorMessage,
                    IsConclusive: result.IsConclusive,
                    IsListed: result.IsListed);
        }

        /*
         * Unveränderliche Ergebnisliste.
         *
         * Die ursprüngliche Reihenfolge des
         * Referenzkatalogs bleibt erhalten.
         */
        return new BlacklistOverview(
            Array.AsReadOnly(entries));
    }
}
