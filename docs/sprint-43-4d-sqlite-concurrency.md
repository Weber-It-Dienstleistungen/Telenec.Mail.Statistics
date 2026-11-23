# Sprint 43.4d – SQLite-Schreibkonkurrenz

Stand: 23.11.2026. Status: **nur Regressionstest im Repository**.

Vor einer kontrollierten Aktivierung der Mail-Security-Historie
pruefen wir konkurrierende `MailSecurityHistoryStore.ProcessAsync`-
Aufrufe auf einer **echten lokalen SQLite-Datei**. Die bisherigen
Tests nutzten einen gemeinsam genutzten In-Memory-Zugang. Diese
Ergaenzung legt dagegen fuer jeden Aufruf einen separaten DbContext
und eine separate SQLite-Verbindung an (Connection Pooling aus).

## Die neue Regression

`IndependentSqliteConnectionsSerializeSnapshotsAsync` prueft:

1. **Gleichzeitiger Erstscan:** Zwei Aufrufe mit gleichem Snapshot
   erzeugen genau eine Baseline, keinen Vorfall.
2. **Gleichzeitige Ablehnungszunahme:** Nur eine Verbindung darf
   denselben Zuwachs als neuen Account-Vorfall buchen.
3. **Unterschiedliche konkurrierende Snapshots:** Der neuere
   Wasserstand bleibt erhalten; insgesamt werden nur die
   tatsaechlichen Differenzen verbucht.

Die Tests nutzen nur temporaere synthetische Daten und entfernen
die Testdateien anschliessend. Das bestehende produktive SQLite-
Schema, die Workerregistrierung und der Plesk-Mailserver werden
durch diesen Commit **nicht** geaendert.

Microsoft.Data.Sqlite unterstuetzt serielle Schreibtransaktionen
und Busy-Wartezeiten. Die bestehende EF-Core-Transaktion wird
bewusst zunaechst unveraendert gelassen. Ein zusaetzliches
plattformuebergreifendes Lock ohne nachgewiesenen Bedarf wuerde
mehr Komplexitaet und eigene Fehlerszenarien schaffen.

## Windows-Pruefung

```powershell
dotnet build .\Telenec.Mail.Statistics.slnx -c Release
dotnet run --project .\Telenec.Mail.Statistics.Tests -c Release --no-build
```

Erwartung: **144 erfolgreich, 0 fehlgeschlagen**.
Diese Zahl ist bis zum Testlauf auf dem Entwicklungsrechner
nicht bestaetigt.

## Verbleibende Einschraenkungen

- Dieser Test simuliert mehrere getrennte Datenbankverbindungen,
  nicht zwei vollstaendig getrennte Betriebssystemprozesse.
- Er beweist keine Ausfallsicherheit bei Prozessabsturz mitten
  in einer Transaktion oder beliebig langer Dateisperre.
- Fuer eine produktive Aktivierung braucht es weiterhin
  Backup, Berechtigungskonzept, Datenschutzfreigabe und ein
  explizit standardmaessig deaktiviertes Konfigurations-Flag.
