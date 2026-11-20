# Telenec Mail Statistics – Mail Security Center

## Sprint 38: Plesk-Datenanbindung, ausschließlich lesend

Der vierte Reiter **Mail-Sicherheit** ist eine getrennte Sicht auf
**Plesk Outgoing Mail Control**. Er soll Missbrauch früh erkennbar machen,
greift aber noch **nicht** in Kundenpostfächer ein.

### Datenquelle

Die Plesk-interne Schnittstelle liefert serverweite Daten:

```bash
plesk sbin mailmng-outgoing --fetch-statistics --with-limits --time-step=hour
```

Erkannte Mailaccount-Felder: `name`, `out_limit`, `stats`.
Erkannte Stundenfelder: `time`, `passed`, `passed_fixed`, `rejected`.

**Wichtig:** Die interne CLI-Schnittstelle kann sich ohne Vorwarnung ändern.
Unbekannte Struktur, fehlerhafte Typen und doppelte Postfächer führen zu
einem Diagnosefehler. `passed_fixed` wird nicht stillschweigend
zu `passed` addiert, solange die fachliche Bedeutung nicht geklärt ist.

`--time-step=hour` liefert Plesk-Berichtszeilen, **keine vollständige
24-Stunden-Historie** aller Accounts. Postfächer ohne Statistikzeilen
werden nicht als nachweislich inaktiv interpretiert. Auch ein
`passed`-Wert bestätigt nicht zwingend die externe Zustellung.

### Collector

`tools/telenec-plesk-security-collector.py`: anonyme Diagnose von
Feldtypen, Limits und Summen. Führt keine Änderungen aus.

`tools/telenec-plesk-security-snapshot.py`: eigener, zunächst ebenfalls
nur lesender Sammler. Erst `--write --output /absoluter/pfad.json`
erstellt einen Snapshot. Die atomar ersetzte Datei erhält
Zugriffsrechte `0600` und ist für `telenecstats` vorgesehen.

Enthalten sind nur Postfächer mit tatsächlich vorhandenen
Statistikzeilen. Der Snapshot enthält dabei personenbezogene
E-Mail-Adressen. Die Daten werden nicht in Rohlogs aus der GUI oder
dem API-Protokoll geschrieben.

Manueller, **bewusst noch nicht automatisierter** Test auf Ubuntu:

```bash
python3 /root/telenec-plesk-security-snapshot.py
```

Erst nach bewusst freigegebenem Einrichtungsschritt:

```bash
python3 /root/telenec-plesk-security-snapshot.py --write --output /var/lib/telenec-mail-statistics/mail-security-preview.json
```

### Statistikserver

Geschützte Lese-API:

```text
GET /api/v1/admin/mail-security
X-Admin-Key: <vorhandener Admin-Schlüssel>
```

Optionale Serverkonfiguration:

```text
MailSecurity__SnapshotPath=/var/lib/telenec-mail-statistics/mail-security-preview.json
```

**Ohne Pfadangabe inaktiv**; gibt `available=false` zurück.
Frischegrenze: 15 Minuten. Datei-Größenlimit: 2 MiB.
Kein Kommando- oder Root-Zugriff aus der ASP.NET-Anwendung.

### Windows-Client

Vierter Reiter: **Mail-Sicherheit**. Die GUI zeigt Anzahl der
erfassten Postfächer, Accounts mit Statistikzeilen, Accounts mit
Ablehnungen, sowie die einzelne Accountstatistik in einer Detailansicht.
Die Auswahl eines Accounts zeigt dessen von Plesk gelieferte
Zeitwerte. Keine Sperrbuttons im Sprint 38.

### Sprint 39: automatische Snapshot-Erneuerung (bereit zur Serverinstallation)

Zwei voneinander unabhaengige systemd-Oneshot-Dienste plus Timer
erneuern die Dateien alle fuenf Minuten:

- `telenec-mail-security-snapshot.service/.timer` ruft die Plesk-CLI
  ueber den vorhandenen Security-Collector auf und schreibt nur eine
  geschuetzte Datei `mail-security-preview.json` mit Postfach-Statistiken.
  Datei: `telenecstats:telenecstats`, Modus `0600`.
- `telenec-mail-traffic-snapshot.service/.timer` liest die Postfix-Logs
  und schreibt `mail-traffic-preview.json` **ohne** Top-3-Accountnamen
  und -Zaehler. Datei: `root:telenecstats`, Modus `0640`.
  Die Option `--omit-account-rankings` ist fuer die anonyme
  Standardkonfiguration des automatischen Collectors gesetzt.
  Die 24 Stundenbalken bleiben erhalten. Die getrennte,
  ausdrueckliche Freigabe der Rankings erfolgt erst ab Sprint 41.

Die Dienste laufen mit Root-Rechten, weil die Plesk-Systemschnittstelle
und Postfix-Protokolle privilegierten Zugriff benoetigen. Sie rufen
ausschliesslich feste, lesende Kommandos auf; ihre einzige Schreibwirkung
ist das atomare Ersetzen von Snapshot-Dateien.

Installation nach bewusstem Download beider Skripte unter `/root`:

```bash
bash /root/Install-MailSnapshotTimers.sh
```

Der Installer prueft die vorhandenen Quelldateien und den Dienstnutzer,
kopiert die Skripte root-owned unter `/usr/local/libexec/`,
legt systemd-Units an und startet beide Collector-Dienste einmalig.
Nur wenn beide erfolgreich waren, aktiviert er die Timer. Keine
Neuveroeffentlichung der .NET-Serveranwendung erforderlich.

Status und Logs:

```bash
systemctl list-timers --all --no-pager | grep 'telenec-mail-.*-snapshot'
journalctl -u telenec-mail-security-snapshot.service -n 30 --no-pager
journalctl -u telenec-mail-traffic-snapshot.service -n 30 --no-pager
```

Die Admin-GUI liest weiter nur Snapshots; sie sendet keine Befehle an
Plesk oder die Collector-Dienste. Der Klick auf `Aktualisieren` laedt
die aktuellen Dateien vom Statistikserver.

Bei Fehlschlag bleibt die letzte gute Datei stehen. Nach 15 Minuten
kennzeichnen beide Server-Reader deren Inhalt als veraltet, statt
den Eindruck frischer Messdaten zu erwecken.

### Sprint 41: gezielt freischaltbare Top-3-Accounts

`tools/Configure-MailTrafficRankings.sh` erlaubt nach gesonderter
Datenschutz-/Berechtigungspruefung die gezielte Aktivierung der schon
implementierten `topSenders`- und `topRecipients`-Felder. Das Script
schreibt ein von der anonymen Standard-Unit getrenntes systemd-Drop-In,
ruft den unveraenderten Traffic-Collector ohne
`--omit-account-rankings` auf und ersetzt einen Snapshot.

Die beiden Timer bleiben unveraendert, ebenso die Sicherheitsansicht
und ihr Plesk-Collector. Das Drop-In bleibt auch bei erneuter
Ausfuehrung des bisherigen Snapshot-Installers erhalten.
Mit `--disable` wird es wieder entfernt und die Datei
ohne Rankings erneuert. Die ergaenzte technische und
datenschutzrechtliche Checkliste liegt in
`docs/release-0.1.2-data-sources.md`.

### Noch NICHT implementiert

- Historisierung ueber die momentanen Snapshot-Zeitreihen hinaus
- Langzeit-Historisierung und Trendanalyse der Plesk-Accountdaten
- Persönliche Administrator-Authentifizierung, Rollen und Audit-Log
- Steuernder Plesk-Agent, manuelle Versandlimits und Entsperrung
- Automatische Missbrauchserkennung und Versandsperren
- Alarmierung des Administrators und Benachrichtigung des Kunden

Diese Funktionen werden erst nach gesonderter Freigabe und
gezielter Prüfung der Berechtigungen schrittweise realisiert.

### Release-Reihenfolge

Ein Commit auf `main` aktualisiert den produktiven Ubuntu-Server
**nicht**. Für den Server ist ein neuer signierter Release und die
separate Installation erforderlich. Die WPF-Ansicht wird erst durch
ein neues Velopack-Paket an MiPo verteilt. Bis dahin laufen die
bereits installierten Versionen unverändert.

Der Server-Updater-Timer wurde beim vorherigen Deployment angehalten;
seinen Zustand und die Wiederaktivierung separat kontrollieren.
