# Sprint 43 – Mail-Security-Risikoanalyse im Beobachtungsmodus

## 43.1: Der erste, absichtlich inaktive Auswertungskern

Stand 20.11.2026. Aktuell produktiv: Statistikserver 0.1.3 und
Windows-Client 0.1.2. Die hier beschriebenen neuen Dateien liegen
zunaechst **nur im Repository**. Ohne kuenftiges signiertes
Serverrelease haben sie keine Auswirkung auf den laufenden Betrieb.

Kern:
`Telenec.Mail.Statistics.Server/MailSecurity/MailSecurityRiskEvaluator.cs`

Regressionstests:
`Telenec.Mail.Statistics.Tests/MailSecurityRiskTests.cs`
(14 Testfaelle, ueber die bestehende Testkonsole eingebunden).

**Keine automatische Versandsperre, keine Aenderung an Plesk-Limits,
kein Mailversand, kein neues API, keine GUI-Aenderung.**

## Datenquelle

Es wird ausschliesslich das bereits validierte
`MailSecurityOverview` aus dem bestehenden
`MailSecuritySnapshotReader` verarbeitet.

Fachlich gelten weiterhin:

- Ein Plesk-`passed` ist kein Nachweis der externen Zustellung.
- Die Plesk-`stats`-Zeilen stellen **keinen garantierten
  lueckenlosen 24-Stunden-Zeitraum** dar.
- Plesk-`time` wird noch nicht als verifizierte UTC-Zeit
  interpretiert.
- Wiederholte, identische Fuenf-Minuten-Snapshots werden **nicht
  miteinander addiert**. Die Engine beurteilt nur EINEN Snapshot.
- Null in einzelnen `passed`-/`rejected`-Feldern bedeutet
  unbekannt und wird nicht als 0 fehlinterpretiert.
- Bei unvollstaendigen Stundenzeilen gibt es keine rechnerisch
  scheinbar praezise Ablehnungsquote.
- Auch ein aktuelles Versandlimit von 0 darf einen historischen
  auffaelligen Befund nicht als aktuell laufenden Angriff darstellen.

## Vorlaeufige Testschwellen

Diese Regeln sind technische Platzhalter und **keine produktiv
freigegebene Spamklassifikation**.

| Stufe | Erstes Signal |
| --- | --- |
| Keine Meldung | Kein belastbarer Hinweis nach aktueller Regel |
| Beobachtung | Mindestens 25 Ablehnungen mit >=10% berechenbarem Anteil oder unbekannter Quote |
| Warnung | Mindestens 100 Ablehnungen mit >=20% Anteil oder unbekannter Quote |
| Kritischer Pruefbedarf | Mindestens 300 Ablehnungen mit >=40% Anteil und >=2 Stundenzeilen mit je >=50 Ablehnungen; alternativ >=500 Ablehnungen mit >=40% Anteil |

Die Stufen sind Untersuchungsprioritaeten, kein Spam-Urteil.
Falls eine Quote nicht berechnet werden kann, wird hoechstens
`Warning` vergeben (nicht `Critical`). Die beobachteten
Stundensummen und die Anzahl betroffener Plesk-Zeilen werden
als reine Belegzahlen am Ergebnis vermerkt.

`OutLimit=0` setzt `SendingLimitZero=true`, loescht aber
nicht automatisch die historischen Ablehnungsdaten.

Ein legitimer Massenversender mit deutlich geringerer Quote
wird bewusst nicht allein wegen seiner Versandmenge markiert.

## Abnahme 43.1 lokal

Nach Pull:

```powershell
dotnet build .\Telenec.Mail.Statistics.slnx -c Release
dotnet run --project .\Telenec.Mail.Statistics.Tests -c Release
```

Die bestehenden Tests muessen gruen bleiben. Dazu kommen
14 neue, rein synthetische Mail-Security-Tests mit zwei
anonymisierten Vergleichsmustern: ein sehr deutlicher und
ein mittelstarker Ablehnungsfall. Es werden **keine
Kundenkonten oder realen Mail-Logs** in die Tests aufgenommen.

## Bevor etwas auf dem Produktivserver aktiviert wird

1. Fachlich die Zeitsemantik der Plesk-Berichtszeilen sowie
   bekannte False Positives pruefen.
2. Event-Identitaet fuer wiederholte Snapshots entwickeln:
   gleiche Plesk-Stunde und Account duerfen keine neue Meldung
   und keinen neuen Vorfall pro Poll erzeugen.
3. Beobachtungszustand (Historisierung, Deduplizierung, Ende
   eines Vorfalls) mit begrenzter Aufbewahrung und getrennten
   Rechten implementieren und testen.
4. API nur lesend und nur mit bestehender Adminauthentifizierung
   bereitstellen, danach GUI-Freigabe.
5. Beobachtungsmodus produktiv ohne Mailaktionen erproben;
   mit dokumentierten Kontofreigaben und Fehlalarmpruefung
   Schwellenwerte kalibrieren.
6. Spaeter gesondert: Alarmierung und kontrollierte
   Versandsperre mit eigenen Sicherheitsfreigaben.

**Wichtig:** Der heute implementierte Evaluator gibt beim
erneuten Auswerten desselben Snapshots dieselbe Bewertung zurueck.
Das ist noch **keine persistente Vorfall-Deduplizierung**. Deshalb
gibt es bewusst noch keinen automatischen Benachrichtigungsdienst
oder periodischen Risikoevaluierungsdienst.
