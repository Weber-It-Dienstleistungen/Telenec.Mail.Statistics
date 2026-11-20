# Sprint 42 – Statistikserver: Backup und Wiederherstellung

Stand: 20.11.2026. Produktiver Server 0.1.3, Windows-Admin 0.1.2.

## Bestandsaufnahme vor Sprint 42

- Produktivdatenbank:
  `/var/lib/telenec-mail-statistics/telenec-mail-statistics.db`
- Dateirechte laut produktivem Screenshot: `telenecstats:telenecstats 0640`;
  Datenbankgroesse am 20.11.: 36.864 Bytes.
- Der Server-Updater ist wieder eingeschaltet und hat die signierte
  `0.1.3` am 20.11. erfolgreich ohne Installation geprueft.
- Drei rollbackbezogene Datenbanksicherungen existieren bereits unter
  `/var/lib/telenec-mail-statistics-updater/backups/`.
- **Die bisherigen Update-Sicherungen sind keine regelmaessigen Backups
  und liegen auf dem gleichen Server.**

## Zielarchitektur

Der neue Backup-Dienst ist **von der Update-Infrastruktur unabhaengig**:

| Komponente | Eigenschaft |
| --- | --- |
| Python-Worker | `tools/telenec-statistics-db-backup.py` |
| Linux-Installer | `tools/Install-StatisticsDatabaseBackup.sh` |
| systemd-Service | `telenec-mail-statistics-db-backup.service` |
| systemd-Timer | `telenec-mail-statistics-db-backup.timer` |
| Datenquelle | Bestehende SQLite-Datei, nur lesend |
| Sicherungsverfahren | SQLite Online Backup API |
| Ziel | `/var/backups/telenec-mail-statistics/` |
| Ausfuehrender Nutzer | `telenecstats` (kein root im Worker) |
| Verzeichnisberechtigung | `0700` |
| Dateiberechtigung | `0600` |
| Zeitplan | taeglich 02:30 UTC, optional bis zu 5 Min. Verzögerung |
| Retention | 14 Tage lokal, Loeschen erst nach Erfolg |
| Kontrolle | `PRAGMA integrity_check` auf der neuen Sicherung |

Die Online-Backup-API erlaubt eine konsistente Sicherung, auch wenn
die Anwendung die Datenbank nutzt (inklusive WAL).
Es werden weder Mailkonten manipuliert noch Dienste neugestartet.

## Sprint 42.1: systemd-Sandbox / SQLite-WAL

Der erste manuelle Test als `telenecstats` mit `runuser` am
20.11.2026 hat erfolgreich eine SQLite-Sicherung mit
`PRAGMA integrity_check=ok` erzeugt. Unter dem systemd-Oneshot
scheiterte derselbe Worker dagegen mit
`unable to open database file` bzw. einem Schreibschutzfehler.

Die wahrscheinliche Ursache ist die Mount-Sandbox:
`ProtectSystem=strict` mit dem urspruenglichen
`ReadOnlyPaths=/var/lib/telenec-mail-statistics`.
SQLite-WAL-Reader koennen trotz `mode=ro` Schreibzugriff
auf das Datenbankverzeichnis benoetigen, um `-shm`- und
`-wal`-Sidecars zu verwalten.

Korrektur am systemd-Dienst in
`tools/Install-StatisticsDatabaseBackup.sh`:

- `ReadWritePaths=/var/lib/telenec-mail-statistics /var/backups/telenec-mail-statistics`
- `ReadOnlyPaths=/var/lib/telenec-mail-statistics/telenec-mail-statistics.db`
- Die Python-Quelle oeffnet die Hauptdatenbank **weiterhin
  ausdruecklich nur lesend** via `mode=ro`.
- Die separate Datenbanksicherungsdatei wird ausschliesslich
  im geschuetzten Backupziel angelegt.
- **Die Schutzanpassung ist noch nicht produktiv nachgetestet.**
  Den Backup-Timer erst nach erfolgreichem Oneshot einschalten.

Der Installer kann nach einem erneuten Download erneut ausgefuehrt
werden; er schreibt nur die beiden eigenen Units und laesst
bereits vorhandene Backupdateien bestehen.

## Installation auf Ubuntu – nur nach Freigabe

Alle folgenden Befehle als root; vor der Installation Quellskripte
vom eigenen, bekannten Repository herunterladen:

```bash
curl -fsSL \
  https://raw.githubusercontent.com/Weber-It-Dienstleistungen/Telenec.Mail.Statistics/main/tools/telenec-statistics-db-backup.py \
  -o /root/telenec-statistics-db-backup.py

curl -fsSL \
  https://raw.githubusercontent.com/Weber-It-Dienstleistungen/Telenec.Mail.Statistics/main/tools/Install-StatisticsDatabaseBackup.sh \
  -o /root/Install-StatisticsDatabaseBackup.sh

python3 -m py_compile /root/telenec-statistics-db-backup.py
bash -n /root/Install-StatisticsDatabaseBackup.sh
bash /root/Install-StatisticsDatabaseBackup.sh
```

Der Installer legt die systemd-Einheiten und das geschuetzte
Backupverzeichnis an, aktiviert aber **keinen Timer**. Der
Updater `telenec-mail-statistics-updater.timer` und die beiden
Mail-Snapshot-Timer bleiben unveraendert.

Zuerst einen einzigen echten, kontrollierten Backupdurchlauf starten:

```bash
systemctl start telenec-mail-statistics-db-backup.service
systemctl status telenec-mail-statistics-db-backup.service --no-pager
journalctl -u telenec-mail-statistics-db-backup.service -n 20 --no-pager
```

`inactive (dead)` bei `Type=oneshot` nach `status=0/SUCCESS` ist
normal. Die Journal-Meldung muss eine neue Datei `SICHERUNG OK` sowie
`Integritaetspruefung: ok` bestaetigen.

Die neueste Sicherung direkt mit dem Dienstnutzer verifizieren:

```bash
runuser -u telenecstats -- \
  /usr/bin/python3 /usr/local/libexec/telenec-mail-statistics/telenec-statistics-db-backup.py --verify-latest
```

Erst wenn beides erfolgreich war:

```bash
systemctl enable --now telenec-mail-statistics-db-backup.timer
systemctl is-active telenec-mail-statistics-db-backup.timer
systemctl list-timers --all --no-pager | grep telenec-mail-statistics-db-backup
```

Bei `Persistent=true` kann beim erstmaligen Start ein nachzuholender
Durchlauf sofort ausgefuehrt werden. Das ist unkritisch, weil das
Skript nur eine zusaetzliche konsistente Sicherung erstellt und
keinen Datenbankstand zurueckschreibt.

## Regelmaessige Betriebskontrolle

```bash
systemctl is-active telenec-mail-statistics-db-backup.timer
journalctl -u telenec-mail-statistics-db-backup.service -n 30 --no-pager
runuser -u telenecstats -- /usr/bin/python3 \
  /usr/local/libexec/telenec-mail-statistics/telenec-statistics-db-backup.py --status
```

Es sollte eine aktuelle Sicherung (hoechstens ca. 26 Stunden alt)
vorliegen; Warnungen und Fehlschlaege muessen bearbeitet werden.
`systemctl` und `journalctl` geben keine Mailinhalte oder
Kunden-Mailadressen aus.

## Wiederherstellung / Notfallplan

**Kein ungetesteter Restore direkt auf der Produktivdatenbank!**

1. Feststellen, welche Sicherung gebraucht wird.
2. Datei vor jeglicher Aktion zunaechst in ein isoliertes
   Testverzeichnis kopieren; dort `PRAGMA integrity_check`
   ausfuehren und erwartete Tabellen und Beispielabfragen pruefen.
3. Bei echtem Produktions-Restore den Statistikdienst stoppen.
   Die vorhandene Datenbank mit etwaigen `-wal`- und `-shm`-
   Dateien unveraendert als forensische Sicherung beiseitelegen.
4. Die freigegebene Sicherung unter dem korrekten Dateinamen mit
   Eigentum `telenecstats:telenecstats` und Modus `0640`
   wiederherstellen; keinesfalls alte WAL/SHM-Dateien unbesehen
   an die restaurierte Datenbank anhaengen.
5. Statistikdienst starten und API-/Datenbank-Gesundheitspruefung
   ausfuehren. Erst dann Vorfall schliessen.

Vor einem echten Restore die Rueckfallstrategie und den genauen
Zeitpunkt mit dem Betriebsverantwortlichen abstimmen. Eine
vollstaendige, isolierte Restore-Probe steht noch aus und bleibt
Bestandteil der Infrastrukturabnahme.

## Wesentliche Restluecke: Offsite-Backup

Die lokalen Sicherungen schuetzen vor Fehlbedienung oder
Datenkorruption, **nicht** vor Ausfall, Verschluesselung oder
Verlust des gesamten Servers.

Erforderlich ist deshalb zusaetzlich eine bestaetigte, verschluesselte
Kopie auf ein unabhaengiges Backupziel, beispielsweise ueber die
vorhandene Firmen-Backupinfrastruktur. Dabei konkret pruefen, ob
Plesk-Backups benutzerdefinierte Pfade unter `/var/backups/`
einschliessen – nicht davon ausgehen!

Abnahme erst nach:

- Einhaltung der Aufbewahrung und Zugriffsbeschraenkungen
- Nachweis einer aktuellen Offsite-Kopie
- Restore-Test aus dieser Offsite-Kopie in einer isolierten Umgebung
- Dokumentierter Wiederanlauf inkl. Zuständigkeiten
