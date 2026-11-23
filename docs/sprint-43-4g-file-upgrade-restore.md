# Sprint 43.4g – Dateibasierte Backup-, Upgrade- und Restore-Probe

Stand: 23.11.2026. Status: **nur synthetische Testdaten,
kein produktives Deployment**.

## Inhalt

Zwei neue Integrationstests nutzen TEMPORAERE SQLite-Dateien statt
einer SQLite-In-Memory-Verbindung:

1. Bestehende Installationsdatenbank mit zwei synthetischen
   Installationen und der alten Migration herstellen.
2. Konsistente Sicherung ueber die SQLite Online Backup API
   erzeugen und mit `PRAGMA integrity_check` pruefen.
3. **Nur eine Kopie** der Sicherung pruefen: Ohne Schemafreigabe
   keine neuen Tabellen, mit ausdruecklicher Freigabe additive
   Migration. Beide Bestandsinstallationen bleiben erhalten.
4. Originaldatei und Backup bleiben unveraendert.
5. Wiederherstellung in eine **separate** Datenbankdatei:
   Integritaet, Installationsdaten und altes Schema erhalten.
   Wiederanlauf mit deaktivierten Flags erzeugt keine
   Mail-Security-History-Tabellen.
6. Unbekannter Migrationsstand in einer gesonderten Datei
   fuehrt zum sicheren Abbruch ohne History-Migration.

Bei den Tests werden ausschliesslich eigene temporaere Dateien
erzeugt und am Ende geloescht.

## Lokale Abnahme

```powershell
dotnet build .\Telenec.Mail.Statistics.slnx -c Release
dotnet run --project .\Telenec.Mail.Statistics.Tests -c Release --no-build
```

Erwartung: **158 Tests erfolgreich, 0 fehlgeschlagen**.
Der Build/Test auf dem Windows-Rechner ist noch ausstehend.

## Abgrenzung

Dies ist **keine** Wiederherstellungsprobe mit einer echten
Server-Sicherung und auch keine Pruefung der Linux-Dateirechte
oder des aktiven systemd-Backup-Timers. Der Backupdienst auf
Ubuntu verwendet Pythons `sqlite3.Connection.backup`;
der Test verwendet denselben SQLite-Online-Backup-Mechanismus
ueber Microsoft.Data.Sqlite. Er testet deshalb den
Sicherungsansatz, nicht den Python-Worker oder seine
systemd-Unit.

Der zuletzt dokumentierte produktive Stand des Backupdiensts
enthaelt noch nicht nachgewiesene Schritte fuer Oneshot,
Timer-Aktivierung und Offsite-Sicherung. Diese Punkte
bleiben ausdruecklich offen.

Kein produktiver Schemawechsel oder Workerstart, bevor Backup,
Restore, Datenschutz, Berechtigungen und Releasefreigabe
tatsaechlich abgenommen wurden.
