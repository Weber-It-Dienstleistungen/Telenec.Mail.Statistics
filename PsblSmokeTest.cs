
#:project ./Telenec.Mail.Statistics.Server/Telenec.Mail.Statistics.Server.csproj
#:property PublishAot=false

using System;
using System.Text;
using Telenec.Mail.Statistics.Server.Reputation;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine(
    "========================================");

Console.WriteLine(
    "Telenec Mail – PSBL Funktionstest");

Console.WriteLine(
    "========================================");

Console.WriteLine();

var checker = new PsblBlacklistChecker();

var targetAddress = "80.79.237.70";

Console.WriteLine(
    $"Prüf-IP: {targetAddress}");

Console.WriteLine(
    "Blacklist: PSBL");

Console.WriteLine(
    "DNSBL: psbl.surriel.com");

Console.WriteLine();

Console.WriteLine(
    "Starte echte Blacklist-Prüfung ...");

Console.WriteLine();

var result = await checker.CheckAsync(
    targetAddress);

PrintResult(result);

Console.WriteLine();

Console.WriteLine(
    "----------------------------------------");

Console.WriteLine(
    "Zusätzlicher Eingabevalidierungstest");

Console.WriteLine(
    "----------------------------------------");

Console.WriteLine();

var invalidResult = await checker.CheckAsync(
    "ungueltige-ip-adresse");

PrintResult(invalidResult);

Console.WriteLine();

var validationPassed =
    invalidResult.Status
        == BlacklistCheckStatus.Failed
    && invalidResult.IsListed is null;

var dnsCheckPassed = result.IsConclusive;

Console.WriteLine(
    "========================================");

Console.WriteLine(
    "TESTERGEBNIS");

Console.WriteLine(
    "========================================");

Console.WriteLine();

Console.WriteLine(
    $"DNSBL-Prüfung eindeutig: " +
    $"{(dnsCheckPassed ? "JA" : "NEIN")}");

Console.WriteLine(
    $"Eingabevalidierung korrekt: " +
    $"{(validationPassed ? "JA" : "NEIN")}");

Console.WriteLine();

if (dnsCheckPassed && validationPassed)
{
    Console.WriteLine(
        "TEST BESTANDEN");

    Console.WriteLine(
        "Die PSBL-Abfrage konnte ausgewertet werden.");

    return 0;
}

Console.WriteLine(
    "TEST NICHT VOLLSTÄNDIG BESTANDEN");

Console.WriteLine(
    "Bitte die angezeigten Einzelmeldungen prüfen.");

return 1;

static void PrintResult(
    BlacklistCheckResult result)
{
    Console.WriteLine(
        $"Blacklist-ID: {result.BlacklistId}");

    Console.WriteLine(
        $"Prüfziel: {result.CheckedTarget}");

    Console.WriteLine(
        $"Status: {result.Status}");

    Console.WriteLine(
        $"Eindeutig: {result.IsConclusive}");

    Console.WriteLine(
        $"Gelistet: {FormatListed(result.IsListed)}");

    Console.WriteLine(
        $"Beginn (UTC): " +
        $"{FormatDate(result.StartedAtUtc)}");

    Console.WriteLine(
        $"Ende (UTC): " +
        $"{FormatDate(result.CompletedAtUtc)}");

    Console.WriteLine(
        $"Provider-Antwort: " +
        $"{result.ProviderResponse ?? "Keine"}");

    Console.WriteLine(
        $"Fehlermeldung: " +
        $"{result.ErrorMessage ?? "Keine"}");
}

static string FormatListed(bool? listed)
{
    return listed switch
    {
        true => "JA",
        false => "NEIN",
        null => "UNBEKANNT"
    };
}

static string FormatDate(
    DateTimeOffset? date)
{
    return date?.ToString(
        "dd.MM.yyyy HH:mm:ss zzz")
        ?? "Nicht vorhanden";
}
