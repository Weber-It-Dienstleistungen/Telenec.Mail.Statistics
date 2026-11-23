# Telenec Mail Statistics – Mailverkehr (Sprint 36)

## Ziel

Der dritte Reiter **Mail-Statistik** zeigt Mailverkehr der letzten 24 Stunden:
24 stündliche Werte für eingehende und ausgehende Nachrichten sowie die
drei meistgenutzten Sender- und Empfänger-Accounts.

**Stand Sprint 41 (20.11.2026):** GUI, geschützte API und produktiver
Postfix/Plesk-Collector sind eingerichtet. Der systemd-Timer
`telenec-mail-traffic-snapshot.timer` aktualisiert den Snapshot
alle fuenf Minuten. Die stündlichen Versand-/Empfangsbalken werden
im Admin-Client 0.1.1 bereits produktiv angezeigt.

Die Top 3 werden im Collector berechnet, aber im produktiven
Standardbetrieb bewusst durch `--omit-account-rankings` aus
dem gespeicherten Snapshot entfernt. Nach entsprechender
Datenschutzfreigabe koennen sie ab Sprint 41 gezielt durch
`tools/Configure-MailTrafficRankings.sh --enable` zugeschaltet
und mit `--disable` wieder ausgeschaltet werden.
Der Rollout fuer Client 0.1.2 steht in
`docs/release-0.1.2-data-sources.md`.

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

Jedes `hourStartUtc` ist `windowStartUtc` plus 0–23 Stunden. Die 24 Balken
beginnen um volle UTC-Stunden (`hh:00`); der letzte ist die **laufende,
noch unvollständige** Stunde. Deshalb darf `windowEndUtc` maximal 60 Minuten
in der Zukunft liegen. Die Erzeugungszeit muss aktuell sein (maximal 15 Minuten
alt). Counts dürfen nicht negativ sein, die Datei darf höchstens 256 KiB
groß sein. Ungültige oder veraltete Snapshots werden **nicht** angezeigt.

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

## Manuelle Diagnose auf dem Plesk-Server

`python3 tools/telenec-mail-traffic-collector.py` führt ausschließlich
lesende Untersuchungen aus und gibt nur Summen und stündliche Zähler aus.
Die Log-Zeitzone wird unter Ubuntu aus `/etc/timezone` oder `/etc/localtime`
ermittelt. Mit `--timezone UTC` beziehungsweise `--timezone Europe/Berlin`
kann sie zur Gegenprüfung fest vorgegeben werden.

Die Ausgabe nennt zusätzlich die Anzahl eindeutiger SASL- und qmgr-Queue-IDs
sowie deren Überschneidung. Explizite `postfix/cleanup ... milter-reject:`-Aktionen
werden als **abgewiesene Nachrichten** getrennt ausgewiesen. Sie sind weder
erfolgreiche SMTP-Einlieferungen noch ungeklärte Queue-IDs. Da dieselbe
Queue-ID bei verschiedenen Vorgängen erneut vorkommen kann, erfolgt die
Zuordnung von SMTP-Annahme, Queue-Bestaetigung und Milter-Ablehnung
zusätzlich anhand ihrer Zeitpunkte (maximal 15 Minuten, begrenzt durch die
nächste beobachtete Annahme mit derselben Queue-ID). Nur wenn innerhalb
dieses konkreten Zeitfensters sowohl `qmgr` als auch Milter-Reject auftreten,
wird der Vorgang als widersprüchlich gekennzeichnet und nicht gezählt.

Wenn mehr als 20 % der verbleibenden, **nicht abgewiesenen** Einlieferungen
weder eine qmgr-Bestätigung noch eine Milter-Ablehnung besitzen, verweigert
der Collector das **Snapshot-Schreiben**. Bei widersprüchlichen IDs wird es
ebenfalls gesperrt. Der reine Diagnoselauf bleibt möglich. Diese Schwelle ist
eine vorläufige Sicherheitsgrenze und ersetzt keine fachliche Validierung.

Die Milter-Diagnose beschreibt die im gewählten Zeitfenster auffindbaren
Logereignisse. Die bisherige über mehrere Logtage aggregierte AWK-Auswertung
ist **nicht** gleichzusetzen mit einer 24-Stunden-Statistik.

## Bugfix-Kandidat 23.11.2026 – Issue #2 (noch nicht produktiv)

Seit 21.11.2026 wurde der produktive
`mail-traffic-preview.json` wegen der mehr als 20 Prozent
**ungeklaerten** SASL-Queue-Einlieferungen nicht mehr aktualisiert.
Die Plesk-Sicherheitsstatistik blieb eigenstaendig aktuell.

Reine Diagnose am 23.11.2026:
204 via `qmgr` bestaetigte Mailannahmen, 7.425
Milter-Ablehnungen und 57 offene Faelle; davon konnten
**28** bei konservativer Session-Zuordnung als
`smtpd disconnect data=0/N` eingegrenzt werden.
19 hatten keine belegte DATA-Abbruchmeldung, 10 waren
nicht eindeutig zuzuordnen. Der Anteil ungeloester
Faelle liegt vor Korrektur ueber der 20-Prozent-Schwelle.

Der neue **noch nicht produktiv aktivierte** Collector auf
`main` klassifiziert deshalb ausschliesslich Sitzungen als
`strict_data_aborted_auth`, wenn:

- genau eine `smtpd`-Prozess-ID zur authentifizierten
  Queue-ID gehoert;
- ein ausdruecklicher `connect` vor und ein `disconnect`
  nach dem Queue-Event innerhalb von 15 Minuten vorliegt;
- der Verbindungsabschluss `data=0/N` mit `N > 0` enthaelt;
- keine andere Queue-ID innerhalb dieser klar abgegrenzten
  SMTPD-Verbindung nachweisbar ist;
- keine mehrdeutigen Verbindungsgraenzen oder dazwischenliegenden
  Verbindungsabschluesse auftreten;
- fuer den betreffenden Versandversuch **weder** eine
  `qmgr`-Bestaetigung **noch** eine erkannte Milter-Ablehnung
  vorliegt.

Solche Sitzungen werden **nicht als versendete Nachrichten
gezaehlt**, aber auch nicht mehr als unerklaerte
Einlieferungen fuer die 20%-Plausibilitaetspruefung
angesehen. Alle anderen ungeklaerten Ereignisse
bleiben ungeklaert; die Schwelle bleibt **unveraendert 20 %**.

Es gibt einen separaten Zaehler fuer die strikt erkannten
Abbrueche. Die bestehenden Versand- und Empfangsdefinitionen,
Top-3-Zaehlweise, Snapshot-Struktur und API bleiben unveraendert.

**Regressionsabnahme vor Serveraenderung:**

```bash
python3 -m unittest discover -s tools/tests -p 'test_mail_traffic_collector.py' -v
python3 -m py_compile tools/telenec-mail-traffic-collector.py
python3 tools/telenec-mail-traffic-collector.py
```

Der letzte Befehl ist **nur Diagnose**, also kein `--write`.
Erst nach erfolgreichen Kunstlog-Tests UND fachlich plausibler
Diagnose auf echten Logs wird die installierte Datei durch
einen getrennt freigegebenen Deploy-Schritt ersetzt.
Die aktuellen Snapshots und Timer werden durch diese
GitHub-Aenderung nicht veraendert.

Bei Ablehnung nach der Diagnose bleibt der Status offen:
keine automatische Grenzwertlockerung oder erzwungene
Snapshot-Aktualisierung.

## Erfuellte Zaehlregeln und bestehende Betriebsgrenzen

Der Collector wurde fuer das aktuelle Postfix/Plesk-Setup
eingerichtet. Vor einem spaeteren Umzug oder Serverwechsel
Logrotation, Zeitzone, Transportwege und Berechtigungen
erneut pruefen. Es gelten weiterhin folgende fachliche Regeln:

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

Der aktuelle Collector aggregiert die Protokolldaten
ausschliesslich auf dem Mailserver zu einem atomar ersetzten
Snapshot. Die serverseitige Erfassung laeuft bereits; die
personenbeziehbaren Top-3-Listen bleiben standardmaessig deaktiviert
und werden erst nach gesonderter Abstimmung freigeschaltet.
