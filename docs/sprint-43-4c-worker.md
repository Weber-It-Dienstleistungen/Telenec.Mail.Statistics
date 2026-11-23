# Sprint 43.4c – Serialisierter Mail-Security-Worker

Stand: 23.11.2026. Status: **nur im Repository vorbereitet, noch
nicht produktiv aktiviert**.

## Umfang

Der Worker liest hoechstens alle 5 Minuten ueber den bereits
validierenden MailSecuritySnapshotReader einen lokalen Plesk-Snapshot.
Ein Durchlauf wird vor dem naechsten vollstaendig abgewartet,
ohne parallele Worker-Schleifen. Er nutzt den in 43.4b getesteten
SQLite-HistoryStore fuer die transaktionale Aktualisierung.

- Kein Hosted-Service-Eintrag in Program.cs.
- Keine produktiven Schema-Migrationen durch diesen Commit.
- Keine automatischen Plesk-Aktionen, Versandsperren, Benachrichtigungen.
- Bei fehlenden, defekten und veralteten Daten wird nichts gespeichert.
- Fehlgeschlagene SQLite-Transaktionen werden zurueckgerollt.
- Workerfehler werden protokolliert, der Server soll weiterlaufen.
- Informationslogs nennen nur Summen, keine Accountkennungen.
- Kein neuer API-Endpunkt und keine GUI-Aenderung.

Der einmalige interne Ausfuehrungspfad ist fuer die Testassembly
freigegeben. Fuer Lebenszyklustests ist intern ein kuerzeres
Timerintervall einstellbar; der normale Konstruktor behaelt
unveraendert das 5-Minuten-Intervall.

Die Lebenszyklustests pruefen nur die **Serialisierung innerhalb
EINER Workerinstanz**, nicht eine spaetere Mehrprozessverteilung
oder parallele Workerinstanzen.

## Testumfang

Sieben synthetische Regressionstests decken ab:
1. gueltiger erster Snapshot nur als Basislinie;
2. echte Ablehnungsdifferenzen und idempotente Wiederholung;
3. fehlende, kaputte und veraltete Quelldateien mit anschliessender Erholung;
4. Fehler bei SQLite-INSERT, atomarer Rollback und erneuter Versuch;
5. Start/Stop ohne Quelle;
6. echte zeitgesteuerte Polls, Deduplizierung und kein Poll nach Stop;
7. langsame Durchlaeufe bleiben im selben Worker strikt seriell.

Nach Pull auf Windows:

```powershell
dotnet build .\Telenec.Mail.Statistics.slnx -c Release
dotnet run --project .\Telenec.Mail.Statistics.Tests -c Release --no-build
```

Erwartung: **143 Tests erfolgreich, 0 fehlgeschlagen**.
Die vier neuen Tests laufen mit einer eigenen SQLite-In-Memory-Datenbank
und temporaeren, ausschliesslich synthetischen JSON-Snapshots.

## Vor Aktivierung / produktivem Serverrelease

- Konkretes Worker-Lebenszyklus-/Restartverhalten testen,
  insbesondere Start/Stop und kontrollierte Einzelinstanz.
- Bei mehreren Prozessen oder zusaetzlichen Schreibzugriffen
  ein DB-weites Serialisierungs-/Retry-Konzept ergaenzen.
- Backup, Datenschutz, Berechtigungen und Sicherheitsfreigabe pruefen.
- Keine Migration des produktiven Statistikservers ohne diese Freigaben.
