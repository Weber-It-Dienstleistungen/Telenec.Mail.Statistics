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

### Noch NICHT implementiert

- Dauerhaft laufender Collector / Aktualisierungstimer
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
