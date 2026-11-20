# Admin-Client 0.1.2: getestete Blacklists und Top-3-Accounts

## Was ist neu?

- Der installierte **Admin-Client 0.1.1** soll sich bei seinem naechsten
  Programmstart **ausschliesslich ueber den Telenec-HTTPS-Updatefeed** auf
  **0.1.2** aktualisieren. Es gibt **kein** neues GitHub-Release.
- Die bestehende Mail-Reputationsanzeige kann nach Freischaltung der
  erweiterten, bereits vorbereiteten DNSBL-Gruppe **21 von 68**
  Referenzquellen aktiv pruefen (8 bisher + 13 erweitert).
- Die bestehende Mail-Statistik-GUI kann drei meistgenutzte authentifizierte
  Versand-Accounts und drei am haeufigsten zugestellte lokale
  Empfaengerpostfaecher fuer dasselbe 24-Stunden-Fenster anzeigen.
- **Mail-Sicherheit und ihre Plesk-Erfassung bleiben unveraendert.**

**Wichtig: Dies sind zwei getrennte Server-Einstellungen. Nur das neue
Windows-Paket zu installieren, aktiviert keine serverseitigen Daten.**
Derzeitiger Server 0.1.3 kann beide Funktionen bereits lesen bzw.
ausfuehren. Es muss dafuer **kein** neues Server-Binary installiert werden.

## Blacklist-Erweiterung (nur getestete Gruppe)

Code `BlacklistMonitoringService` unterstuetzt bereits:

- `Reputation:EnableExtendedDnsbl=true`: 13 weitere oeffentliche
  DNSBL-Profile, insgesamt **21** inklusive PSBL;
- `Reputation:EnableReviewDnsbl=false`: 5 gesonderte, noch nicht
  bestaetigte Kandidaten bleiben aus;
- weitere 42 Katalogreferenzen sind weiterhin nicht integriert.

In `Telenec.Mail.Statistics.Server/appsettings.json` ist die
getestete Erweiterungsgruppe ab Sprint 41 standardmaessig **true**.
**Achtung:** Ein GitHub-Commit aendert die laufende produktive
Serverkonfiguration nicht. Bis zum naechsten signierten Serverrelease
muss dieselbe Einstellung in der produktiven Umgebung explizit
gesetzt werden.

Vor dem Editieren an Ubuntu prüfen, ob der Schluessel bereits gesetzt ist
(keine Secrets aus `server.env` ausgeben):

```bash
grep '^Reputation__Enable' /etc/telenec-mail-statistics/server.env || true
```

Bei fehlendem Schluessel:

```bash
printf '\nReputation__EnableExtendedDnsbl=true\nReputation__EnableReviewDnsbl=false\n' >> /etc/telenec-mail-statistics/server.env
systemctl restart telenec-mail-statistics.service
systemctl is-active telenec-mail-statistics.service
```

Falls die Schluessel bereits existieren: **vorhandene Eintraege
gezielt bearbeiten statt doppelte Werte anzuhängen**. Die
Dateiberechtigungen nicht veraendern. Danach Admin-Client starten
und `Mailserver-Reputation → Jetzt pruefen` ausfuehren. Erwartet:
`21 von 68 Referenzlisten angebunden`, unabhaengig davon, ob
alle 21 Checks erfolgreich antworten. Ein Fehlschlag oder eine
Zugriffssperre einer DNSBL muss weiterhin als Fehler dargestellt
werden, niemals als `Nicht gelistet`.

Weder die uebrigen 42 Quellen noch die 5 Review-Kandidaten werden
durch diesen Schritt aktiviert. Aktuelle Anbieterbedingungen und
Kontrollabfragen vor spaeterer Erweiterung erneut pruefen.

## Top-3-Versand/Empfang: bestehender Collector, expliziter Schalter

Die Python-Erfassung berechnet die Top 3 bereits, leert beide Listen
bei den automatischen produktiven Snapshots aber absichtlich durch
`--omit-account-rankings`. Die geschuetzte Admin-API und WPF-GUI
lesen die Listen bereits ohne Codeaenderungen.

**Datenschutz:** Accountadressen sind personenbezogen bzw.
personenbeziehbar. Vor produktiver Aktivierung Zweckbindung,
berechtigten Administratorkreis, Dokumentation/VVT,
Datenschutzhinweise und konkrete Aufbewahrung mit dem
Datenschutzbeauftragten abstimmen. **Die bisherige Opt-in-Einwilligung
zur Client-Nutzungsstatistik ist nicht die Rechtsgrundlage der
serverseitigen Mailverkehrsauswertung.**

Die serverseitigen Postfix-Logdaten bleiben auf dem Mailserver.
Das nur fuer `root:telenecstats` lesbare Snapshotfile wird alle
fuenf Minuten atomar ersetzt; der lesende Admin-Endpunkt erfordert
`X-Admin-Key`. Es werden keine Betreffzeilen, Nachrichtentexte,
Attachments, SMTP-Kennwoerter oder Rohlogs an die GUI gesendet.

Die standardmaessige Snapshot-Unit bleibt absichtlich anonym.
**Erst nach Freigabe** das Repo-Skript
`tools/Configure-MailTrafficRankings.sh` auf dem Ubuntu-Server
(z.B. als `/root/Configure-MailTrafficRankings.sh`) bereitstellen.

Vorherige Einstellung prüfen:

```bash
bash /root/Configure-MailTrafficRankings.sh --status
```

Gezielt aktivieren (ausschliesslich den Traffic-Collector; andere
Dienste und Timer werden nicht umkonfiguriert):

```bash
bash /root/Configure-MailTrafficRankings.sh --enable
```

Das Skript erzeugt ein systemd-Drop-In, entfernt nur das Argument
`--omit-account-rankings` aus dem aufgerufenen Traffic-Collector,
startet den Traffic-Oneshot neu und gibt nur Anzahl der Rankingzeilen
aus, keine Accountnamen. Der Security-Collector bleibt unveraendert.
Die Original-Unit und die beiden Timer bleiben intakt.

Danach:

```bash
bash /root/Configure-MailTrafficRankings.sh --status
systemctl is-active telenec-mail-traffic-snapshot.timer telenec-mail-security-snapshot.timer
```

Erwartet sind `3` Eintraege fuer `topSenders` und `topRecipients`,
wenn genuegend unterschiedliche Accounts in den Logs vorkommen.
Die Admin-GUI zeigt dann die Accountnamen mit Nachrichtenzahl.

Sofortige Ruecknahme bei Bedarf:

```bash
bash /root/Configure-MailTrafficRankings.sh --disable
```

Dies entfernt ausschliesslich das eigene Drop-In und schreibt wieder
einen Snapshot mit leeren Rankinglisten. Wegen der Speicherung im
geschuetzten Snapshot Verzeichnisschutz und Administratorzugriffe
vor Aktivierung nochmals kontrollieren.

## Windows-Build / Velopack-Release

Die Paketkennung der bereits installierten Clients und ihr Channel
sind bestaetigt:

```text
PackId: Telenec.Mail.Statistics.Admin
Channel: test
Installed version: 0.1.1
Next version: 0.1.2
```

Auf Windows `main` pullen, zuerst Build/testen, dann im
Repository-Verzeichnis:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\Build-StatisticsAdminRelease.ps1" -ExistingPackId "Telenec.Mail.Statistics.Admin" -ExistingChannel "test"
```

Ausgabe:

```text
publish/statistics-admin-releases/0.1.2/
```

**Ausschliesslich Telenec-Server:**
`/var/www/vhosts/necnet.de/dav.necnet.de/updates/statistics-admin/`

Die neuen `*.nupkg` und Setup zuerst hochladen, dann
`assets.test.json`, `RELEASES-test`,
**`releases.test.json` zuletzt**. Kein GitHub-Release mehr.

Es ist sinnvoll, auf dem Webserver nur das freigegebene
0.1.2-Release zu publizieren, die alten Dateien nicht voreilig zu
loeschen, und per GET / SHA256 zunaechst wieder Feed und Paket zu
verifizieren.

Der in 0.1.1 installierte Velopack-Code benutzt die Telenec-URL:
`https://dav.necnet.de/updates/statistics-admin/`.
**Zum End-to-End-Test** den installierten Client 0.1.1 ganz
schliessen und neu starten (kein `dotnet run`). Nach dem
automatischen Einspielen muss die GUI Version `0.1.2` anzeigen.
Nur so ist der vollstaendige neue Updatepfad belegt.

Der Update-Server-Task `telenec-mail-statistics-updater.timer`
war separat angehalten und wird durch diesen Client-Rollout **nicht**
automatisch wieder aktiviert.
