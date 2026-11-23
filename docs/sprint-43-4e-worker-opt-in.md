# Sprint 43.4e – Kontrollierte Worker-Aktivierung (Opt-in)

Stand: 23.11.2026. Status: **nur im Repository; kein produktiver Rollout**.

## Verhalten

`MailSecurity:EnableHistoryWorker` ist in
`Telenec.Mail.Statistics.Server/appsettings.json` explizit `false`.

`Program.cs` ruft
`AddMailSecurityHistoryWorkerIfEnabled(configuration)` nach der
vorhandenen Datenbankinitialisierung und der bisherigen
Aufbewahrungsbereinigung auf:

- Nicht gesetzter Schluessel oder `false`: kein Worker registriert.
- Explizit `true` und konfigurierter `MailSecurity:SnapshotPath`:
  einmalige Registrierung des bestehenden
  `MailSecurityHistoryWorker` als `IHostedService`.
- `true` ohne SnapshotPath: Fehler beim Start, keine Registrierung.
- Ungueltige Werte (z. B. `yes`): Fehler beim Start, keine
  unbeabsichtigte Aktivierung.

Keine neuen Aufgaben im Mailserver, keine Versandlimits,
keine Benachrichtigungen, keine Plesk-Schreibzugriffe.

## Testschritt

Fuenf neue synthetische Konfigurationstests pruefen fehlenden,
deaktivierten, aktivierten und ungueltigen Zustand sowie
doppelte Registrierung. Es wird kein echter Worker gestartet.

```powershell
dotnet build .\Telenec.Mail.Statistics.slnx -c Release
dotnet run --project .\Telenec.Mail.Statistics.Tests -c Release --no-build
```

Erwartung: **149 Tests erfolgreich, 0 fehlgeschlagen**.
Bis zum lokalen Testlauf nicht bestaetigt.

## Produktivsperre: Migration wird davon NICHT gesteuert!

Der vorhandene `ServerInitializationHostedService` ruft beim
Serverstart **unabhaengig vom Worker-Flag** weiterhin
`Database.MigrateAsync()` auf. Ein spaeteres Serverrelease
mit den 43.4b-Migrationen wuerde also auch bei deaktiviertem
Worker die neuen Account-Historientabellen anlegen.

**Dieser Commit darf deshalb noch nicht produktiv ausgerollt
werden.** Vor Freigabe: Backup/Restore-Probe, Rechtsgrundlage
und Aufbewahrung, Dateiberechtigungen, Datenbankmigration,
Mehrprozessverhalten und kontrolliertes Releaseverfahren
abnehmen. Kein aktivierendes Setting in `appsettings.json`
einchecken. Fuer eine spaetere Aktivierung ist eine
bewusste Betriebsentscheidung und separate
Umgebungskonfiguration erforderlich.
