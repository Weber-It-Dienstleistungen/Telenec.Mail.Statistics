# Sprint 43.4h – Datenschutz-Prüfvorlage: Mail-Security-Vorfallhistorie

**Stand:** 23.11.2026 · **Status:** technische Vorlage für DSB/Rechtsstelle und Betrieb, **keine Datenschutzfreigabe**, kein Deployment. In diesem Sprint nur Dokumentation, keine Programmänderungen.

## 1. Ziel und Abgrenzung

Die zukünftige Verarbeitung soll eine **interne, rein beobachtende** Sicherheitsbeurteilung ermöglichen: aus Plesk Outgoing Mail Control werden neue abgewiesene Versandversuche je Mailaccount erkannt und als mögliche Untersuchungshinweise aggregiert.

- Kein Spamnachweis und kein Beweis einer Kontokompromittierung: `rejected` ist nur ein Plesk-Zähler; `passed` beweist keine externe Zustellung.
- Keine Nachrichtentexte, Betreffzeilen, Empfängerlisten, Anhänge, Passwörter oder IP-Adressen in der neuen Vorfalltabelle.
- Keine automatische Versandsperre, Änderung der Plesk-Limits, Kundenbenachrichtigung, Warn-E-Mail oder sonstige Plesk-Schreiboperation.
- Die bereits laufende Momentaufnahme der Plesk-Mailstatistik und die geplante **persistente Historie** sind zu unterscheiden. Das Client-Opt-in zur Installations-/Nutzungsstatistik ist **keine** Einwilligung für diese serverseitige Sicherheitsverarbeitung.

## 2. Verifizierter Datenfluss (aktueller GitHub-Code)

1. Bestehender Plesk-Snapshot-Collector erzeugt eine geschützte lokale JSON-Datei mit Postfachkennungen, Stundenkennungen und Passed-/Rejected-Werten.
2. `MailSecuritySnapshotReader` akzeptiert nur validierte Dateien bis 2 MiB mit höchstens 15 Minuten Alter. Fehlen/veraltet/ungültig bedeutet `unavailable`, nicht Entwarnung.
3. Zukünftiger `MailSecurityHistoryWorker` verarbeitet beim Start und danach etwa alle fünf Minuten seriell. Er ist **standardmäßig aus** (`MailSecurity:EnableHistoryWorker=false`).
4. Anlage der Historientabellen ist separat **standardmäßig gesperrt** (`MailSecurity:AllowHistorySchemaMigration=false`).
5. `MailSecurityHistoryStore.ProcessAsync` schreibt Checkpoint und Vorfälle gemeinsam in einer SQLite-Transaktion. Wiederholte und ältere Snapshots dürfen nicht mehrfach zählen.
6. Für die **gespeicherte Vorfallhistorie** gibt es bislang **keine veröffentlichte Read-API und keine GUI**. Die bestehenden Account-/Risiko-Snapshot-APIs nutzen den gemeinsamen `X-Admin-Key`, noch keine personenbezogenen Admin-Rollen oder ein gesondertes Zugriffs-Audit.

## 3. Präzise Datenkategorien

| Datenort | Persistenz / Datenfelder | Einordnung |
| --- | --- | --- |
| Bestehende lokale Plesk-Snapshotdatei | vollständige Mailaccount-Kennung, originaler Plesk-Stundenbezeichner, `passed`, `rejected`, `out_limit`, Erfassungszeit | personenbezogen/personenbeziehbar |
| `MailSecurityCheckpointStates` | ein Datensatz mit `UpdatedAtUtc` und `CheckpointJson`; JSON enthält je Account+Stunde höchste bekannte Zählerstände und letzte Beobachtungszeit | **auch Accounts ohne Ablehnungen** sind enthalten |
| `MailSecurityIncidents` | UUID, normalisierte Accountkennung, erste/letzte Beobachtung, ggf. Abschlusszeit, `NewPassed`, `NewRejected`, Prioritätsstufe, Merkmal Versandlimit 0 | personenbezogene bzw. personenbeziehbare Sicherheitsmetadaten |

**Nicht anonym:** Eine E-Mail-Adresse kann eine natürliche Person unmittelbar oder mittelbar identifizieren. Auch Funktionspostfächer sollten nicht pauschal als anonym gelten. Die neue Historie würde in derselben SQLite-Datenbank wie die Bestands-Installationsstatistik liegen, allerdings in getrennten Tabellen.

## 4. Bewertungs- und Aufbewahrungslogik – entscheidende Einschränkungen

- Interne Vorfall-Priorität (nicht Spamklassifizierung): `None` bei 1–24 neuen Ablehnungen; `Observe` ab 25; `Warning` ab 100; `Critical` ab 500. Auch `None` kann als interner Beobachtungsdatensatz entstehen.
- `MailSecuritySnapshotChangeDetector`: Watermarks pro Account/Stunde für **72 Stunden nach letzter Beobachtung**; Entfernen **nur bei weiterer gültiger Verarbeitung**.
- Vorfälle werden nach **mehr als 24 Stunden ohne neue Ablehnung** beim **nächsten gültigen Snapshot** geschlossen.
- Geschlossene Fälle werden **30 Tage nach Abschluss**, ebenfalls **erst bei einem nächsten gültigen Snapshot**, gelöscht.
- Bei dauerhaft ausgefallener Snapshotquelle oder offenen Fällen gibt es bislang **keine harte kalenderbasierte Löschung**; ein separater Cleanup fehlt. Eine Zahl von 30 Tagen als garantierte Speicherhöchstdauer wäre sachlich falsch.
- Bestehendes lokales Backup: täglich per SQLite Online Backup API, **14 Tage** Aufbewahrung; am 23.11.2026 waren Timer, Integrität und eine isolierte Wiederherstellung einer echten Serversicherung erfolgreich geprüft.
- Hostspezial führt eine externe Sicherung des Servers durch. **Ob genau diese SQLite-Datenbank/Backups enthalten sind**, ihre externe Retention sowie Restore-/Zugriffsrechte sind noch explizit zu bestätigen. Auch gelöschte Daten können bis zum Ablauf der Backupfristen fortbestehen.

## 5. Bereits vorhandene TOMs und verbleibende Zugriffsfragen

- Gesperrte Worker-/Migrations-Flags; validierter, rein lesender Snapshot-Reader; transaktionale Deduplizierung; partieller UNIQUE-Index (maximal ein offener Fall je Account); 158 erfolgreiche synthetische Regressionstests.
- Der Worker protokolliert regulär nur Zählsummen, keine Accountnamen. **Ausnahme- und Diagnoseprotokolle** sowie externe Backup-Protokolle müssen dennoch auf unbeabsichtigte Accountdaten untersucht werden.
- Auf Ubuntu am 23.11.2026 geprüft: produktives DB-Verzeichnis `0750`, DB-Datei `0640`, Backupverzeichnis `0700`, Backup-Datei `0600`; jeweils `telenecstats`. Die Gruppe zeigte keine expliziten Zusatzmitglieder; Konten mit dieser primären GID wurden damit nicht vollständig ausgeschlossen.
- Noch keine separate Rechte-/Rollenprüfung und kein personenbezogenes Zugriffsprotokoll für eine spätere History-API. Der vorhandene geteilte Admin-Schlüssel sollte nicht unbesehen als ausreichende Berechtigung für **Langzeitdaten** gelten.
- Künftige Admin-Anzeige: nur für konkret zuständige Administratoren, zweckgebunden und mit minimalem Datenumfang; Freigabe nötig.

## 6. Rechtliche Bewertung ausdrücklich offen

**Nicht allein Art. 6 DSGVO prüfen.** Bei einem Anbieter von Mail-/Telekommunikationsdiensten kann das **Fernmeldegeheimnis gemäß § 3 TDDDG** auch die näheren Umstände und erfolglose Kommunikationsversuche schützen. **§ 9 TDDDG** schränkt die Verwendung von Verkehrsdaten ein; **§ 12 TDDDG** enthält besondere Regeln für Störungs- und Missbrauchsbekämpfung. Ob die konkreten Plesk-Zähler und Accountkennungen unter diese Normen fallen, welche rechtliche Erlaubnis besteht und welche Speicherfrist dafür zulässig ist, muss der DSB / die Rechtsstelle klären. Ein mögliches berechtigtes Interesse nach Art. 6 Abs. 1 lit. f DSGVO ist **keine automatische Ersatzgrundlage** für telekommunikationsrechtliche Spezialregelungen. Art. 32 DSGVO allein ersetzt ebenfalls keine Verarbeitungsbefugnis.

**Zur fachlichen Entscheidung vorlegen:**

1. Sind Postfachkennungen, Plesk-Stundenwerte und aggregierte Ablehnungen hier Verkehrsdaten, Steuer-/Sicherheitsdaten oder andere Daten? Sind TDDDG §§ 3, 9, 12 einschlägig?
2. Trägt die Rechtsgrundlage ein **permanentes, accountweites Monitoring** oder nur anlassbezogene Verarbeitung bei konkret festgestelltem Missbrauchsverdacht? Wäre Datensparsamkeit durch kürzere oder pseudonymisierte Checkpoints geboten?
3. Welche verantwortliche Stelle verarbeitet diese Daten, in welcher Rolle handeln Dienstleister (einschließlich Hostspezial)? Welche vertraglichen Regelungen oder AV-Verträge sind einschlägig?
4. Sind 72h Account-Watermarks, der Beginn der Vorfallaufzeichnung **bereits ab einer Ablehnung**, 24h Inaktivitätsabschluss und 30 Tage nach Schließung erforderlich? Welche **harte Löschobergrenze** gilt bei Ausfällen und offenen Fällen?
5. Welche Speicherfristen und Zugriffsrechte gelten für lokale und externe Backups? Wie werden Betroffenenrechte und Löschungen in Backup-Systemen berücksichtigt?
6. Ist der gemeinsame Admin-Key für die geplante Historienansicht ausreichend oder sind personalisierte Logins, Rollen und Zugriffsaudits erforderlich?
7. Welche Einträge/Änderungen an VVT, Datenschutzhinweisen, TOMs sowie welche Datenschutz-Folgenabschätzungs-Vorprüfung sind notwendig?
8. Wie muss gegenüber Administratoren und Kunden kenntlich gemacht werden, dass eine `Critical`-Priorität **kein Spamurteil** ist? Automatische Sperren/Benachrichtigungen wären ein **neuer**, gesondert freizugebender Verarbeitungsschritt.

## 7. Freigabegates / nächster technischer Schritt

1. DSB-/Rechtsbewertung zur Rechtmäßigkeit, zum Zweck, zur Notwendigkeit und zur Speicherdauer einholen. **Bis dahin kein produktiver History-Worker.**
2. **Sicherungslücke schließen:** Zeitgesteuerte Löschung auch ohne gültige Snapshots sowie klare Grenze für offene Vorfälle; danach erneute Regressionstests.
3. Umfang und Restorefähigkeit der Hostspezial-Offsite-Sicherung für die relevanten Pfade bestätigen.
4. Anforderungen an eine später mögliche History-API einschließlich Authentifizierung, Rollen, Audit und Zugriffsschutz separat spezifizieren.
5. Erst nach Freigabe signiertes Release mit zunächst **beiden Flags aus** kontrolliert prüfen; Schema und Worker jeweils bewusst und getrennt aktivieren. Kein automatischer Mailserver-Eingriff.

## 8. Quellen

**Code:** `MailSecuritySnapshotReader.cs`, `MailSecuritySnapshotChangeDetector.cs`, `MailSecurityHistoryStore.cs`, `MailSecurityHistoryEntities.cs`, `MailSecurityHistoryWorker.cs`, `MailSecurityHistoryWorkerRegistration.cs`, `MailSecuritySchemaMigrationPolicy.cs`, `AdminEndpoints.cs`, `StatisticsDbContext.cs`; `docs/statistics-server-backup-recovery.md`.

**Rechtlicher Prüfrahmen:**
- [DSGVO – EUR-Lex](https://eur-lex.europa.eu/eli/reg/2016/679/oj)
- [TDDDG § 3 – Fernmeldegeheimnis](https://www.gesetze-im-internet.de/ttdsg/__3.html)
- [TDDDG § 9 – Verkehrsdaten](https://www.gesetze-im-internet.de/ttdsg/__9.html)
- [TDDDG § 12 – Missbrauch und Störungen](https://www.gesetze-im-internet.de/ttdsg/__12.html)
- [EDPB Leitlinien 1/2024 zu Art. 6 Abs. 1 lit. f DSGVO](https://www.edpb.europa.eu/documents/guideline/guidelines-12024-on-processing-of-personal-data-based-on-article-61f-gdpr_en)
