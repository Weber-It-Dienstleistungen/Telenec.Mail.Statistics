namespace Telenec.Mail.Statistics.Server.MailSecurity;

/// <summary>
/// Singleton-Zustand fuer den zuletzt dauerhaft verarbeiteten
/// gueltigen Plesk-Snapshot. Erst zusammen mit der Vorfallhistorie
/// in einer SQL-Transaktion schreiben.
/// </summary>
public sealed class MailSecurityCheckpointState
{
    public int Id { get; set; } = 1;
    public string CheckpointJson { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Nur aggregierte neu beobachtete Zaehler, keine Nachrichteninhalte.
/// Ein Eintrag bleibt pro Account offen, bis 24 Stunden lang keine
/// weiteren Ablehnungen beobachtet wurden. "None" ist lediglich ein
/// interner Beobachtungsdatensatz, KEIN Spam-Vorfall.
/// </summary>
public sealed class MailSecurityIncident
{
    public Guid Id { get; set; }
    public string Account { get; set; } = string.Empty;
    public DateTime FirstObservedUtc { get; set; }
    public DateTime LastObservedUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public long NewPassed { get; set; }
    public long NewRejected { get; set; }
    public MailSecurityRiskLevel MaxRiskLevel { get; set; }
    public bool SendingLimitZero { get; set; }
}
