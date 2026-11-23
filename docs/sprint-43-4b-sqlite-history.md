# Sprint 43.4b – SQLite-Checkpoint und interne Vorfallhistorie

Stand: 23.11.2026. Status: **auf GitHub vorbereitet,
Regressionstests auf Entwicklerrechner noch ausstehend**.

Die bereits produktive Mail-Statistik wurde am 23.11.2026
gesondert repariert (Issue #2). Die neue Mail-Security-
Historisierung ist davon **unabhaengig**.

## Abgrenzung

- Nutzt **die bestehende Statistik-SQLite-Datenbank**
  und das EF-Core-Migrationsverfahren.
- Kein neuer automatisch laufender Dienst.
- Kein Schreiben in Plesk, keine Konto-/Versandsperren,
  keine E-Mails, keine Kundenbenachrichtigungen.
- Noch keine API zum Abrufen der Verlaufshistorie und
  keine GUI-Aenderungen.
- Keine Nachrichtentexte, Betreffzeilen, Empfaengerlisten
  oder IP-Adressen in der neuen Historientabelle.

### Datenbankmigration

`20261123070000_AddMailSecurityHistory` fuegt ausschliesslich
zwei Tabellen hinzu:

1. `MailSecurityCheckpointStates`: ein Zustand mit
   serialisiertem zeitlichem 72h-Checkpoint und UTC-Zeitpunkt.
   Der JSON-Checkpoint enthaelt notwendigerweise Account-
   Kennungen sowie die Plesk-Stundenbezeichner und deren
   bisherige Hoechstzaehler.
2. `MailSecurityIncidents`: ID, normalisierte
   Accountkennung, erster/letzter beobachteter Zuwachs,
   Abschlusszeit, neu beobachtete Passed-/Rejected-Zaehler,
   Pruefprioritaet und Versandlimit-0-Merkmal.

Eine **partielle UNIQUE-Bedingung** auf Account bei
`ClosedAtUtc IS NULL` verhindert mehr als einen
offenen Fall derselben normalisierten Kontokennung.
Bestehende Installationstabellen bleiben unveraendert.

**Release-Hinweis:** Der vorhandene Statistikserver ruft
beim Start `Database.MigrateAsync` auf. Ein spaeteres
Serverrelease mit dieser Migration wuerde deshalb die
neuen Tabellen anlegen. Vor einem Release daher Backup,
Datenschutz-/Berechtigungspruefung und Tests abschliessen.
Bisher wurde nichts produktiv aktualisiert.

## Auswertungsverhalten

`MailSecurityHistoryStore.ProcessAsync(snapshot)`
verwendet die vorhandene reine
`MailSecuritySnapshotChangeDetector.Compare`-Logik.

- Erster gueltiger Snapshot: **Basislinie**, kein Vorfall.
- Ungueltiger, fehlender oder nicht neuerer Snapshot:
  **keine Schreiboperation**.
- Nur **neu beobachtete Rejected-Differenzen** erzeugen
  bzw. aktualisieren interne Account-Beobachtungen.
- Reine Passed-Zunahme aktualisiert den Checkpoint,
  erzeugt aber keinen Sicherheitsfall.
- Zusaetzliche Rejected-Differenzen werden in denselben
  noch offenen Account-Fall aufsummiert. Ein Polling desselben
  Snapshots zaehlt nichts erneut.
- Nach mehr als **24 Stunden ohne weitere Ablehnungen**
  wird ein offener Fall bei der naechsten gueltigen
  Verarbeitung abgeschlossen. Ein neuer Zuwachs danach
  erzeugt einen neuen Fall mit neuer ID.
- **30 Tage nach Abschluss** wird ein Fall beim naechsten
  gueltigen Snapshot geloescht. Die Checkpoint-Watermarks
  verfallen nach **72 Stunden**. Eine separate zeitgesteuerte
  Bereinigung bei komplett ausgefallener Datenquelle
  ist noch nicht angebunden.
- Checkpoint, Abschluss, neue/aktualisierte Faelle und
  Bereinigung liegen **in einer SQLite-Transaktion**.
  Schlaegt ein Teil fehl, darf auch der Checkpoint
  nicht fortgeschrieben werden.

### Vorlaeufige Prioritaet ausschliesslich aus neuen Ablehnungen

| Neu beobachtete Ablehnungen innerhalb desselben Falls | Interne Prioritaet |
| --- | --- |
| 0 bis 24 | `None`: interner Eintrag, kein Alarm |
| ab 25 | `Observe` |
| ab 100 | `Warning` |
| ab 500 | `Critical` (Pruefbedarf, kein Spamnachweis) |

Hier **keine** scheinbar praezise Ablehnungsquote verwenden:
Die Passed-Differenzen bilden nur den Teil der Berichtszeilen
ab, in denen zugleich Rejected-Differenzen vorlagen.
Der bestehende Bestands-Risk-Evaluator hat eine
andere Datenbasis und darf nicht zur Eskalation einzelner
neu beobachteter Vorgaenge missbraucht werden.

Die Schwellen sind **Prototypwerte**, nicht produktiv
kalibriert. Keine automatische Alarmierung, auch nicht
bei `Critical`.

## Regressionstests

`Telenec.Mail.Statistics.Tests/MailSecurityHistoryStoreTests.cs`
enthaelt **15 echte SQLite-In-Memory-Integrationstests** mit
synthetischen Accountdaten. Sie pruefen u. a.:

- additive Migration mit bestehenden Tabellen sowie
  Upgrade eines bestueckten `Installations`-Bestands,
- historische Basislinie ohne falschen Vorfall,
- erste Zunahme und weitere Aktualisierung,
- wiederholte/alte Snapshots und neue DbContext-Instanz,
- fehlende Snapshotdaten,
- Schliessen, erneutes Oeffnen, 30-Tage-Bereinigung,
- atomaren Rollback bei absichtlich ausgeloestem
  SQLite-Schreibfehler,
- Limit 0, historische Prioritaet vs. neue Deltas,
  Stufengrenzen, reine Passed-Zunahmen und Lesen.

Auf dem Windows-Entwicklungsrechner nach Pull:

```powershell
dotnet build .\Telenec.Mail.Statistics.slnx -c Release
dotnet run --project .\Telenec.Mail.Statistics.Tests -c Release
```

**Erwartung:** 136 Tests erfolgreich (121 bisherige + 15 neue),
null fehlgeschlagen. Ein gruener Build wurde fuer diesen
neuen Code noch nicht bestaetigt.

## Naechster Schritt nach gruenen Tests

Den Historienprozessor nur in **einem serialisierten
Hintergrundworker** betreiben. Mehrere gleichzeitig
schreibende SQLite-Verbindungen benoetigen ein
gesondertes Lock-/Retry-Konzept, bevor periodische
Verarbeitung freigegeben wird. Anschliessend
Lese-API, Authentifizierung und GUI separat abnehmen.

Auch die Rechtsgrundlage, Zugriffsbeschraenkungen,
Aufbewahrung und Dokumentation der accountbezogenen
Security-Historie muessen vor dem produktiven Rollout
mit den zustaendigen Stellen abgestimmt werden.
