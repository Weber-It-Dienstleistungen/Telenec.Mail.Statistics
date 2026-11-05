
using System.Net;
using System.Net.Sockets;

namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Prüft IPv4-Adressen gegen die Passive Spam Block List.
///
/// DNSBL-Zone: psbl.surriel.com
///
/// Der Dienst führt ausschließlich lesende DNS-Abfragen aus.
/// Er verändert keine Mailserver- oder DNS-Konfiguration.
///
/// Eine negative Antwort gilt nur dann als "Nicht gelistet",
/// wenn zuvor die PSBL-Kontrollabfragen erfolgreich waren.
/// </summary>
public sealed class PsblBlacklistChecker
{
    private const string BlacklistId = "psbl";
    private const string DnsZone = "psbl.surriel.com";

    private static readonly TimeSpan QueryTimeout =
        TimeSpan.FromSeconds(8);

    private static readonly IPAddress ListedResponse =
        IPAddress.Parse("127.0.0.2");

    /// <summary>
    /// Prüft eine IPv4-Adresse gegen PSBL.
    ///
    /// Aufrufende Komponenten können die Prüfung
    /// über einen CancellationToken abbrechen.
    /// </summary>
    public async Task<BlacklistCheckResult> CheckAsync(
        string ipv4Address,
        CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;

        BlacklistCheckResult CreateResult(
            BlacklistCheckStatus status,
            string? providerResponse = null,
            string? errorMessage = null)
        {
            return new BlacklistCheckResult
            {
                BlacklistId = BlacklistId,
                CheckedTarget = ipv4Address ?? string.Empty,
                Status = status,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ProviderResponse = providerResponse,
                ErrorMessage = errorMessage
            };
        }

        if (!IPAddress.TryParse(
                ipv4Address,
                out var targetAddress)
            || targetAddress.AddressFamily
                != AddressFamily.InterNetwork)
        {
            return CreateResult(
                BlacklistCheckStatus.Failed,
                errorMessage:
                    "Die angegebene Adresse ist keine gültige IPv4-Adresse.");
        }

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutSource.CancelAfter(QueryTimeout);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Positive Kontrollabfrage:
            // 127.0.0.2 muss bei PSBL gelistet sein.

            var positiveTest = await QueryAsync(
                $"2.0.0.127.{DnsZone}",
                timeoutSource.Token);

            if (positiveTest.NameNotFound
                || !positiveTest.Addresses.Contains(
                    ListedResponse))
            {
                return CreateResult(
                    BlacklistCheckStatus.Failed,
                    errorMessage:
                        "Die positive PSBL-Kontrollabfrage " +
                        "lieferte nicht das erwartete Ergebnis.");
            }

            // Negative Kontrollabfrage:
            // 127.0.0.1 darf bei PSBL nicht gelistet sein.
            //
            // Dadurch erkennen wir beispielsweise
            // fehlerhafte DNS-Wildcard-Antworten.

            var negativeTest = await QueryAsync(
                $"1.0.0.127.{DnsZone}",
                timeoutSource.Token);

            if (!negativeTest.NameNotFound)
            {
                return CreateResult(
                    BlacklistCheckStatus.Failed,
                    errorMessage:
                        "Die negative PSBL-Kontrollabfrage " +
                        "lieferte ein unerwartetes Ergebnis.");
            }

            // Die DNS-Kontrollabfragen waren erfolgreich.
            // Jetzt wird die tatsächliche Ziel-IP geprüft.

            var addressBytes =
                targetAddress.GetAddressBytes();

            var queryName =
                $"{addressBytes[3]}." +
                $"{addressBytes[2]}." +
                $"{addressBytes[1]}." +
                $"{addressBytes[0]}." +
                DnsZone;

            var response = await QueryAsync(
                queryName,
                timeoutSource.Token);

            // NXDOMAIN bedeutet bei einer funktionierenden
            // PSBL-DNSBL, dass kein Eintrag existiert.

            if (response.NameNotFound)
            {
                return CreateResult(
                    BlacklistCheckStatus.NotListed,
                    providerResponse: "NXDOMAIN");
            }

            var responseText = string.Join(
                ", ",
                response.Addresses.Select(
                    address => address.ToString()));

            // PSBL verwendet 127.0.0.2 als
            // bestätigten positiven Rückgabewert.

            if (response.Addresses.Contains(
                    ListedResponse))
            {
                return CreateResult(
                    BlacklistCheckStatus.Listed,
                    providerResponse: responseText);
            }

            // Andere Antworten werden ausdrücklich
            // nicht als erfolgreiche Negativprüfung
            // oder als bestätigtes Listing gewertet.

            return CreateResult(
                BlacklistCheckStatus.Unknown,
                providerResponse: responseText,
                errorMessage:
                    "PSBL lieferte einen nicht eindeutig " +
                    "interpretierbaren DNS-Rückgabewert.");
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return CreateResult(
                BlacklistCheckStatus.Failed,
                errorMessage:
                    "Die PSBL-DNS-Abfrage hat das " +
                    "Zeitlimit von 8 Sekunden überschritten.");
        }
        catch (OperationCanceledException)
        {
            // Ein ausdrücklich vom Aufrufer
            // angeforderter Abbruch wird weitergegeben.

            throw;
        }
        catch (SocketException exception)
        {
            return CreateResult(
                BlacklistCheckStatus.Failed,
                errorMessage:
                    "DNS-Abfrage fehlgeschlagen: " +
                    exception.SocketErrorCode);
        }
        catch (Exception exception)
        {
            return CreateResult(
                BlacklistCheckStatus.Failed,
                errorMessage:
                    "PSBL-Prüfung fehlgeschlagen: " +
                    exception.Message);
        }
    }

    /// <summary>
    /// Führt eine IPv4-DNS-Auflösung durch.
    ///
    /// Nur HostNotFound wird als negativer
    /// DNSBL-Nachweis interpretiert.
    ///
    /// Andere DNS-Probleme bleiben Fehler
    /// und werden nicht als NXDOMAIN behandelt.
    /// </summary>
    private static async Task<DnsQueryResult> QueryAsync(
        string queryName,
        CancellationToken cancellationToken)
    {
        try
        {
            var addresses =
                await Dns.GetHostAddressesAsync(
                    queryName,
                    AddressFamily.InterNetwork,
                    cancellationToken);

            return new DnsQueryResult(
                NameNotFound: false,
                Addresses: addresses);
        }
        catch (SocketException exception)
            when (exception.SocketErrorCode
                == SocketError.HostNotFound)
        {
            return new DnsQueryResult(
                NameNotFound: true,
                Addresses: Array.Empty<IPAddress>());
        }
    }

    /// <summary>
    /// Internes DNS-Abfrageergebnis.
    /// </summary>
    private sealed record DnsQueryResult(
        bool NameNotFound,
        IPAddress[] Addresses);
}
