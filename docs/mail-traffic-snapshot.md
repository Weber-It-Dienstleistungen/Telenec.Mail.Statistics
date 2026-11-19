# Telenec Mail Statistics – Mailverkehr (Sprint 36)

## Ziel

Der dritte Reiter **Mail-Statistik** zeigt Mailverkehr der letzten 24 Stunden:
24 stündliche Werte für eingehende und ausgehende Nachrichten sowie die
drei meistgenutzten Sender- und Empfänger-Accounts.

**Stand Sprint 36:** GUI und geschützte Lese-API sind implementiert.
Ein Collector, der echte Daten des Mailservers aggregiert, ist noch nicht
angeschlossen. Die Oberfläche zeigt bis dahin ausdrücklich „Datenquelle
nicht eingerichtet“ statt vermeintlicher Nullwerte.

## Lese-Schnittstelle

`GET /api/v1/admin/mail-traffic` mit `X-Admin-Key`.
Der Server liest nur dann eine Snapshot-Datei, wenn
`MailTraffic:SnapshotPath` konfiguriert ist. Ohne gültigen, frischen Snapshot:
HTTP 200 mit `available: false`, Hinweistext und leeren Werten.
Für ältere Server ohne den Endpunkt zeigt die GUI eine entsprechende Meldung.

Snapshot-Datei: UTF-8-JSON, **keine rohen Protokolle**.
Die Konfiguration kann über die produktive Umgebung oder (nur lokal) über
User Secrets erfolgen. Der Pfad ist kein Parameter eines öffentlichen API-Aufrufs.

## JSON-Vertrag

Die Schlüssel heißen:

- `generatedAtUtc`: Erzeugungszeitpunkt als ISO-8601 mit UTC-Offset
- `windowStartUtc` und `windowEndUtc`: exakt 24 Stunden Abstand
- `hours`: exakt 24 aufsteigend sortierte Einträge
  `{ "hourStartUtc": "...", "outgoing": 0, "incoming": 0 }`
- `topSenders`: 0–3 Einträge `{ "account": "konto@example.org", "count": 12 }`
- `topRecipients`: 0–3 Einträge im selben Format

Jedes `hourStartUtc` ist `windowStartUtc` plus 0–23 Stunden.
Counts dürfen nicht negativ sein. Die Datei darf höchstens 256 KiB groß sein.
Generierung und Ende des Zeitfensters müssen jeweils höchstens 15 Minuten
zurückliegen. Ungültige oder veraltete Snapshots werden **nicht** angezeigt.

Ein Beispiel mit zwei Stunden (schematisch, **keine gültige vollständige Datei**):

```json
{
  "generatedAtUtc": "2026-11-19T07:00:00Z",
  "windowStartUtc": "2026-11-18T07:00:00Z",
  "windowEndUtc": "2026-11-19T07:00:00Z",
  "hours": [
    {"hourStartUtc": "2026-11-18T07:00:00Z", "outgoing": 14, "incoming": 22},
    {"hourStartUtc": "2026-11-18T08:00:00Z", "outgoing": 9, "incoming": 16}
  ],
  "topSenders": [
    {"account": "beispiel@example.org", "count": 12}
  ],
  "topRecipients": [
    {"account": "postfach@example.org", "count": 18}
  ]
}
```

## Anforderungen an die spätere Datenquelle

**Die eigentliche Arbeit steht noch aus.** Erst das reale MTA-Setup prüfen:
Postfix/Qmail, Logspeicherort, Logrotation, Zeitzone, mehrere Mailserver,
Zustelltransport und Berechtigung des technischen Collector-Prozesses.

Für belastbare Kennzahlen gelten folgende fachliche Regeln:

1. **Ausgehend:** Erfolgreich angenommene, authentifizierte SMTP-Submissions
   von lokalen Accounts anhand der Authentifizierungsidentität
   (nicht blind anhand eines fälschbaren `From`-Headers). Eine Queue-ID
   nicht mehrfach zählen, bloße Wiederholungen/Zustellversuche ausschließen.
2. **Eingehend:** Für lokale Postfächer erfolgreich zugestellte E-Mails.
   Bei mehreren Empfängern bewusst pro lokales Empfängerpostfach zählen.
   Weiterleitungen und interne Umleitungen fachlich separat prüfen.
3. **Top 3:** Dasselbe Zeitfenster und dieselbe Zähllogik wie die Diagramme.
   Ein hoher Wert ist nur ein Untersuchungsanlass, kein Spam-Beweis.
4. **Keine Inhalte:** Weder Betreff, Nachrichtentext, Attachments noch
   Rohlogs oder Kennwörter in die Statistikdatenbank übertragen.
5. **Datenschutz/DSB:** Personenbeziehbare Accountnamen, Zweckbindung,
   Zugriffsrechte, Verarbeitungsverzeichnis und Aufbewahrung vor dem
   produktiven Anbinden abstimmen. Nicht dieselbe Einwilligungslogik
   der Client-Nutzungsstatistik stillschweigend übernehmen.

Der künftige Collector kann auf dem Mailserver aus Logs aggregieren und
nur den kompakten Snapshot atomar ersetzen. Bis zur ausdrücklichen
Produktivfreigabe gibt es **keine automatische Protokollauswertung**.
