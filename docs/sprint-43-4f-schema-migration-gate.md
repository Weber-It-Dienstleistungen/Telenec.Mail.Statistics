# Sprint 43.4f – Separate Freigabe fuer Mail-Security-Schemamigration

Stand: 23.11.2026. Status: **nur auf GitHub vorbereitet**, kein
produktives Serverrelease und keine veraenderten Produktivdaten.

## Motivation

Der bestehende Statistikserver ruft `Database.MigrateAsync()` beim
Start auf. Das wuerde die in Sprint 43.4b hinzugefuegten
accountbezogenen Tabellen auch dann anlegen, wenn der Worker per
`MailSecurity:EnableHistoryWorker=false` ausgeschaltet ist.

## Neue Sicherheitsregel

`MailSecurity:AllowHistorySchemaMigration` steht in
`appsettings.json` standardmaessig auf `false`.

Der Startdienst nutzt nun `MailSecuritySchemaMigrationPolicy`:
- Auf einer neuen Datenbank wird ohne Freigabe nur
  `20260930113558_InitialCreate` angewendet. Die vorhandene
  Nutzungsstatistik bleibt dadurch funktionsfaehig.
- Auf bestehenden Datenbanken mit bereits angelegten
  Installationstabellen werden ohne Freigabe **keine neuen
  Migrationen** ausgefuehrt.
- Erst `AllowHistorySchemaMigration=true` erlaubt die additive
  Mail-Security-Migration. Die Workeraktivierung bleibt davon
  getrennt und standardmaessig deaktiviert.
- Wenn das History-Schema bereits installiert ist, setzt
  `false` dieses niemals zurueck; insbesondere wird
  `MigrateAsync(InitialCreate)` dann NICHT aufgerufen.
- Ein Workerstart ohne vorhandenes History-Schema und ohne
  Migrationsfreigabe ist ein Konfigurationsfehler.
- Unbekannte neue Migrationen erfordern eine bewusste
  Anpassung der Freigabepolitik vor einem Release.

Damit wird keine Versandaktion ausgeloest und keine
mailbezogene Kontodatei im Code untersucht.

## Regression

Sieben neue In-Memory-SQLite-Tests pruefen Neuinstallation,
Datenbestand, additive Freigabe, kein Down-Migration,
fehlende/erlaubte Workerfreigabe und ungueltige Flags.

```powershell
dotnet build .\Telenec.Mail.Statistics.slnx -c Release
dotnet run --project .\Telenec.Mail.Statistics.Tests -c Release --no-build
```

**Erwartung: 156 erfolgreich, 0 fehlgeschlagen.**
Ergebnis ist vor dem Testlauf nicht bestaetigt.

## Noch vor Produktivrollout

Die Datenbank sollte zusaetzlich auf einer echten SQLite-Datei
aus einem aktuellen Backup mit dokumentierter Restore-Probe
getestet werden. Der hier implementierte Startschutz ist
kein Ersatz fuer Sicherung, Rechtepruefung, Datenschutzfreigabe
und ein bewusstes signiertes Release.
