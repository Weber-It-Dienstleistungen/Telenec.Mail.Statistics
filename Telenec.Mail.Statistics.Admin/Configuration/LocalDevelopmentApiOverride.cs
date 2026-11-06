
namespace Telenec.Mail.Statistics.Admin.Configuration;

/// <summary>
/// Zentraler, ausschließlich für Debug-Builds
/// aktivierbarer lokaler Entwicklungsmodus.
///
/// Die Aktivierung erfolgt über eine temporäre
/// Umgebungsvariable mit einem Test-Admin-Schlüssel.
///
/// Serveradresse und Schlüssel werden immer
/// gemeinsam umgeschaltet.
///
/// Es werden keine gespeicherten Einstellungen
/// oder Windows-Zugangsdaten verändert.
/// </summary>
internal static class LocalDevelopmentApiOverride
{
    private static readonly Uri LocalServerUri =
        new("http://localhost:5097/");

#if DEBUG
    private const string DevelopmentKeyVariable =
        "TELENEC_STATISTICS_DEV_ADMIN_KEY";

    private static readonly string? DevelopmentApiKey =
        ReadDevelopmentApiKey();
#endif

    /// <summary>
    /// Der Entwicklungsmodus ist nur aktiv,
    /// wenn es sich um einen Debug-Build handelt
    /// und ein gültiger Testschlüssel vorliegt.
    /// </summary>
    public static bool IsActive
    {
        get
        {
#if DEBUG
            return DevelopmentApiKey is not null;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Fest definierte lokale Entwicklungsadresse.
    /// </summary>
    public static Uri BaseUri => LocalServerUri;

    /// <summary>
    /// Liefert ausschließlich im aktiven
    /// Debug-Testmodus den temporären API-Schlüssel.
    /// </summary>
    public static string GetAdminApiKey()
    {
#if DEBUG
        return DevelopmentApiKey
            ?? throw new InvalidOperationException(
                "Der lokale Entwicklungsmodus ist nicht aktiviert.");
#else
        throw new InvalidOperationException(
            "Der lokale Entwicklungsmodus ist in Release-Builds deaktiviert.");
#endif
    }

#if DEBUG
    /// <summary>
    /// Liest den Testschlüssel einmalig beim
    /// ersten Zugriff während der Programmlaufzeit.
    ///
    /// Dadurch bleibt die Konfiguration
    /// während einer Sitzung unverändert.
    /// </summary>
    private static string? ReadDevelopmentApiKey()
    {
        var apiKey =
            Environment.GetEnvironmentVariable(
                DevelopmentKeyVariable);

        if (apiKey is null)
        {
            return null;
        }

        byte[] decodedKey;

        try
        {
            decodedKey =
                Convert.FromBase64String(apiKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Der lokale Test-Admin-Schlüssel ist kein gültiger Base64-Wert.",
                exception);
        }

        if (decodedKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Der lokale Test-Admin-Schlüssel muss mindestens 32 Bytes lang sein.");
        }

        return apiKey;
    }
#endif
}
