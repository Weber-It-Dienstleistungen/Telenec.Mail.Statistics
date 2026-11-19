# Admin-Client: Velopack von GitHub auf Telenec-Server migrieren

Stand: 19. November 2026 – Sprint 40, Clientversion 0.1.1.

## Ziel / Architektur

Bestehende Windows-Installationen des Admin-Panels (`0.1.0-test.3`)
sollen ohne Neuinstallation auf den aktuellen Stand mit vier Reitern kommen.
Ab diesem Release stammen spaetere Client-Updates ausschliesslich vom
von Telenec betriebenen HTTPS-Feed:

```text
https://dav.necnet.de/updates/statistics-admin/
```

**Der Pfad ist als neuer VHost-Ordner vorgesehen; seine HTTP-Erreichbarkeit
muss vor dem Rollout geprueft werden.** Das schon bestehende
Server-Release-Verzeichnis `updates/statistics-server/` wird davon
nicht veraendert. Die signierte Linux-Server-Release-Infrastruktur ist
ein **anderes Produkt** als der Windows-Velopack-Updater.

Die Admin-API-Anmeldedaten sind vom Client-Updatekanal getrennt.
Die Velopack-Verteildateien duerfen keine Zugangsdaten enthalten.
Das HTTPS-Feed und die zugehoerigen Pakete muessen ohne Login von
Windows aus erreichbar sein, weil `SimpleWebSource` hier keine
Client-Authentifizierung konfiguriert hat.

## Warum eine GitHub-Bruecke unvermeidbar ist

Der bereits installierte Client `0.1.0-test.3` enthaelt einen fest
eingebauten `GithubSource` und fragt dort beim Programmstart nach
Updates. Eine Aenderung in GitHub-Quellcode oder ein Upload nur auf den
neuen HTTP-Server koennen diesen bereits installierten Client nicht
umleiten.

Deshalb **einmalig dasselbe Bridge-Release 0.1.1 auf beide Feeds bringen**:

1. Server-Webverzeichnis vorbereiten und externen HTTPS-Zugriff pruefen.
2. Auf Windows Quellcode `main` pullen und Anwendung bauen.
3. Urspruenglichen Velopack-`packId` und `channel` der
   produktiv installierten Version `0.1.0-test.3` aus deren
   `sq.version` oder der bisherigen `releases.win.json` pruefen.
   **Nicht aus dem C#-Projektnamen erraten.** Dieser Wert muss beim
   Paketieren unveraendert bleiben, sonst ist es keine In-Place-Migration.
4. Client `0.1.1` packen. Der Packaging-Helper verlangt explizit
   `-ExistingPackId`, damit die Paketidentitaet bewusst bestaetigt wird.
5. Das neu erzeugte `releases.win.json` und die in dessen Feed
   referenzierten `*-full.nupkg` zunaechst auf den Telenec-HTTPS-Host
   laden. Setup.exe kann fuer Neuinstallation dort liegen.
   Die Full-Packages **vor** der Indexdatei hochladen.
6. Von einem Windows-Rechner pruefen, dass
   `https://dav.necnet.de/updates/statistics-admin/releases.win.json`
   mit HTTP 200, gueltigem JSON und dem erwarteten PackId antwortet.
   Download eines vollstaendigen Pakets testen, nicht bloss HTTP HEAD.
7. Erst **danach** dasselbe `0.1.1`-Release als letztes
   GitHub-Release auf
   `Weber-It-Dienstleistungen/Telenec.Mail.Statistics` veroeffentlichen.
   Dieses GitHub-Release muss den bisherigen Windows-`win`-Kanal
   bedienen und tatsaechlich **published** statt draft sein.
8. Den alten Client `0.1.0-test.3` starten. Er laedt das Bridge-Release
   noch von GitHub. Danach ist die installierte Anwendung `0.1.1`,
   die ab jetzt nur noch den Telenec-HTTPS-Feed konsultiert.
9. Bei MiPo mit der bestehenden Installation die vier Reiter und
   den weiterhin gespeicherten Admin-API-Zugang pruefen.
10. Ein **spaeteres** Release `0.1.2` ausschliesslich ueber den
    Telenec-Host veroeffentlichen; erst durch dessen automatisches
    Einspielen ist der vollstaendige Wechsel praktisch nachgewiesen.
    Ab diesem Zeitpunkt wird fuer Clientupdates kein GitHub-Release
    mehr benoetigt.

**Wichtig:** Alle Pakete eines Releases muessen exakt dieselben sein,
nicht zweimal unter derselben Versionsnummer neu gepackt werden.

## Paketieren auf Windows

Die bestehende Paketidentitaet zuerst aus einem alten Release
ermitteln. Beispiel, wenn die alte Feed-Datei lokal vorliegt:

```powershell
((Get-Content '.\Releases\releases.win.json' -Raw | ConvertFrom-Json).Assets | Select-Object -First 1).PackageId
```

Wenn dort kein altes Feed existiert, aus der aktuellen Installation
`sq.version` oder den bisherigen GitHub-Release-Assets ermitteln.
Die bisherige `win`-Channel-Kennung ebenfalls bestaetigen. Wenn
sie abweicht, Paketierung zuerst darauf abstimmen.

Anschliessend:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\tools\Build-StatisticsAdminRelease.ps1" -ExistingPackId "<EXAKTER_BISHERIGER_PACKID>"
```

Der Helfer erstellt einen `win-x64`-Release-Build und das Velopack
Setup/Update-Paket unter:

```text
publish/statistics-admin-releases/0.1.1/
```

Vor dem Hochladen: `releases.win.json` oeffnen, die referenzierten
Dateien pruefen und Paketidentitaet gegen bestehende Installation
vergleichen. **Keine Artefakte einer anderen App oder eines anderen
Channels in den Feed mischen.**

Das Script baut lokal und fuehrt keine Veroeffentlichung, Uploads
oder Serververaenderungen aus.

## Plesk-Zielordner

Geplanter Dateisystempfad:

```text
/var/www/vhosts/necnet.de/dav.necnet.de/updates/statistics-admin/
```

Web-URL:

```text
https://dav.necnet.de/updates/statistics-admin/
```

**Ob das Verzeichnis ueber nginx/Apache wirklich HTTP-lesbar ist, muss
extern getestet werden.** Der bestehende signierte Server-Updater
greift nur auf lokale Dateien unter `statistics-server/` zu und
beweist keine Erreichbarkeit des neuen Windows-Feeds.

Verzeichnislisten abschalten und Upload-/Schreibrechte eng begrenzen.
Nur verifizierte Releases aus vertrauenswuerdiger Quelle dort ablegen.
HTTPS-/Zertifikatsfehler duerfen nicht umgangen werden. Keine
privaten Schluessel oder Admin-Tokens in dieses Verzeichnis kopieren.

## Beispiel fuer HTTP-Test von Windows

```powershell
Invoke-RestMethod "https://dav.necnet.de/updates/statistics-admin/releases.win.json" | Select-Object -ExpandProperty Assets | Select-Object PackageId, Version, FileName
```

Falls die Feed-Felder abweichen, Rohformat pruefen. Danach mit
`vpk download http --url
https://dav.necnet.de/updates/statistics-admin/`
den tatsaechlichen Downloadpfad testen.

## Betriebs- und Sicherheitsaspekte

- Der App-Updater prueft nur bei Programmstart. Ein offenes Fenster
  aktualisiert sich **nicht** selbststaendig auf eine neue Softwareversion.
- Fehler beim Updatecheck verhindern weiterhin nicht den Programmstart.
- Verzeichnis `%LOCALAPPDATA%/Telenec/Mail Statistics/Admin/settings.json`
  und Windows Credential Manager
  (`Telenec.Mail.Statistics.Admin:AdminApiKey`) behalten dieselben
  Namen: keine erneute Einrichtung allein wegen der neuen Version.
- Bei Ausfall des GitHub-Updaters vor der Bruecke bleibt als Notweg
  ein manueller Start des identisch paketierten neuen Setup-Installers,
  nicht das Aendern eines Registry-Eintrags.
- Ein statischer Updatefeed auf dem Server muss besonders gegen
  unbefugte Datei-Aenderungen geschuetzt werden. Die HTTPS-Uebertragung
  und Velopack-Hashpruefung ersetzen keine Vertrauens- und
  Code-Signierungsstrategie fuer einen Admin-Client. Windows
  Authenticode-Signatur fuer produktive breite Verteilung vorsehen.
- Das Mail-Security-Panel in 0.1.1 ist nur **lesend**. Spamsperren
  werden nicht automatisch durch dieses Update aktiviert.
- Der Linux-Statistikserver 0.1.3 und beide Mail-Snapshot-Timer
  benoetigen wegen dieses Clientupdates **keine** neue Installation.
