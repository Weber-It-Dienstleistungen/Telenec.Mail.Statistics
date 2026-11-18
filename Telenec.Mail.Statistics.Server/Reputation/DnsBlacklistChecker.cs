
using System.Net;
using System.Net.Sockets;

namespace Telenec.Mail.Statistics.Server.Reputation;

/// <summary>
/// Technisches Profil einer IPv4-DNSBL.
///
/// Ein Profil darf erst für aktive Abfragen
/// verwendet werden, wenn DNS-Zone,
/// Antwortcodes, Kontrolladressen und
/// Anbieterbedingungen geprüft wurden.
///
/// PositiveControlAdditionalReturnCodes:
/// Zusätzliche Codes, die ausschließlich bei
/// der positiven Kontrollabfrage erlaubt sind.
///
/// Diese Codes werden ausdrücklich NICHT
/// als echte Blacklist-Listungen gewertet.
///
/// Die Definition allein aktiviert keine Abfrage.
/// </summary>
public sealed record DnsBlacklistProfile(
    string BlacklistId,
    string DnsZone,
    IReadOnlyCollection<string> ListedReturnCodes,
    IReadOnlyCollection<string> AccessDeniedReturnCodes,
    string PositiveControlAddress,
    string NegativeControlAddress,
    IReadOnlyCollection<string>?
        PositiveControlAdditionalReturnCodes = null);

/// <summary>
/// Ergebnis einer einzelnen DNS-A-Abfrage.
///
/// NXDOMAIN wird ausdrücklich von anderen
/// DNS-Fehlern unterschieden.
/// </summary>
public sealed record DnsBlacklistDnsResponse(
    bool IsNxDomain,
    IReadOnlyList<IPAddress> Addresses);

/// <summary>
/// Austauschbare DNS-Schnittstelle.
///
/// Dadurch können wir die Prüfengine
/// mit simulierten DNS-Antworten testen,
/// ohne echte Blacklist-Anbieter abzufragen.
/// </summary>
public interface IDnsBlacklistResolver
{
    Task<DnsBlacklistDnsResponse> QueryAsync(
        string queryName,
        CancellationToken cancellationToken);
}

/// <summary>
/// Produktiver DNS-Resolver auf Basis
/// der System-DNS-Auflösung.
///
/// Nur HostNotFound wird als NXDOMAIN
/// interpretiert.
///
/// NoData und andere DNS-Fehler bleiben
/// ausdrücklich technische Fehler.
/// </summary>
public sealed class SystemDnsBlacklistResolver
    : IDnsBlacklistResolver
{
    public async Task<DnsBlacklistDnsResponse> QueryAsync(
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

            return new DnsBlacklistDnsResponse(
                IsNxDomain: false,
                Addresses: Array.AsReadOnly(addresses));
        }
        catch (SocketException exception)
            when (exception.SocketErrorCode
                  == SocketError.HostNotFound)
        {
            return new DnsBlacklistDnsResponse(
                IsNxDomain: true,
                Addresses: Array.Empty<IPAddress>());
        }
    }
}

/// <summary>
/// Gemeinsame Prüfengine für IPv4-DNSBLs
/// mit positiven und negativen Kontrollabfragen.
///
/// Die positive Kontrollabfrage darf
/// anbieterabhängig zusätzliche, bekannte
/// Kontrollcodes liefern.
///
/// Diese zusätzlichen Kontrollcodes werden
/// niemals automatisch als Listung bewertet.
///
/// Der Checker führt nur auf ausdrücklichen
/// Aufruf von CheckAsync DNS-Abfragen durch.
/// </summary>
public sealed class DnsBlacklistChecker
{
    private static readonly TimeSpan QueryTimeout =
        TimeSpan.FromSeconds(8);

    private readonly string _blacklistId;
    private readonly string _dnsZone;

    private readonly IPAddress _positiveControlAddress;
    private readonly IPAddress _negativeControlAddress;

    private readonly HashSet<IPAddress> _listedCodes;
    private readonly HashSet<IPAddress> _accessDeniedCodes;
    private readonly HashSet<IPAddress> _positiveControlCodes;

    private readonly IDnsBlacklistResolver _resolver;

    public DnsBlacklistChecker(
        DnsBlacklistProfile profile,
        IDnsBlacklistResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(
                profile.BlacklistId))
        {
            throw new ArgumentException(
                "Die Blacklist-Kennung darf nicht leer sein.",
                nameof(profile));
        }

        if (string.IsNullOrWhiteSpace(
                profile.DnsZone))
        {
            throw new ArgumentException(
                "Die DNSBL-Zone darf nicht leer sein.",
                nameof(profile));
        }

        var dnsZone =
            profile.DnsZone.Trim().TrimEnd('.');

        if (!IsValidDnsZone(dnsZone))
        {
            throw new ArgumentException(
                "Die DNSBL-Zone ist ungültig.",
                nameof(profile));
        }

        _blacklistId = profile.BlacklistId;
        _dnsZone = dnsZone;

        _positiveControlAddress =
            ParseIpv4(
                profile.PositiveControlAddress,
                "positive Kontrolladresse");

        _negativeControlAddress =
            ParseIpv4(
                profile.NegativeControlAddress,
                "negative Kontrolladresse");

        if (_positiveControlAddress.Equals(
                _negativeControlAddress))
        {
            throw new ArgumentException(
                "Positive und negative Kontrolladresse " +
                "müssen unterschiedlich sein.",
                nameof(profile));
        }

        /*
         * Echte Listungscodes.
         *
         * Nur diese Codes dürfen bei einer
         * tatsächlichen Zielprüfung zu Listed führen.
         */
        _listedCodes =
            ParseReturnCodes(
                profile.ListedReturnCodes,
                "Listungscodes",
                requireNonEmpty: true);

        /*
         * Zugriffssperren werden getrennt
         * von tatsächlichen Listungen bewertet.
         */
        _accessDeniedCodes =
            ParseReturnCodes(
                profile.AccessDeniedReturnCodes,
                "Zugriffssperrcodes",
                requireNonEmpty: false);

        if (_listedCodes.Overlaps(
                _accessDeniedCodes))
        {
            throw new ArgumentException(
                "Listungscodes und Zugriffssperrcodes " +
                "dürfen sich nicht überschneiden.",
                nameof(profile));
        }

        /*
         * Positive Kontrollabfragen dürfen
         * grundsätzlich alle echten Listungscodes
         * enthalten.
         *
         * Standardverhalten aller bisherigen
         * Anbieter bleibt damit unverändert.
         */
        _positiveControlCodes =
            new HashSet<IPAddress>(_listedCodes);

        /*
         * Manche Anbieter liefern bei der
         * Testadresse zusätzliche bekannte
         * Reputationscodes.
         *
         * Beispiel Mailspike:
         * 127.0.0.13 steht für L2.
         *
         * Dieser Code darf eine positive
         * Kontrollabfrage nicht ungültig machen.
         *
         * Er ist aber kein automatisch
         * freigegebener Listungscode.
         */
        if (profile.PositiveControlAdditionalReturnCodes
            is not null)
        {
            var additionalCodes =
                ParseReturnCodes(
                    profile.PositiveControlAdditionalReturnCodes,
                    "zusätzliche positive Kontrollcodes",
                    requireNonEmpty: false);

            if (additionalCodes.Overlaps(
                    _listedCodes))
            {
                throw new ArgumentException(
                    "Zusätzliche Kontrollcodes dürfen " +
                    "keine bereits definierten " +
                    "Listungscodes wiederholen.",
                    nameof(profile));
            }

            if (additionalCodes.Overlaps(
                    _accessDeniedCodes))
            {
                throw new ArgumentException(
                    "Zusätzliche Kontrollcodes dürfen " +
                    "keine Zugriffssperrcodes enthalten.",
                    nameof(profile));
            }

            _positiveControlCodes.UnionWith(
                additionalCodes);
        }

        _resolver =
            resolver ?? new SystemDnsBlacklistResolver();
    }

    /// <summary>
    /// Prüft eine IPv4-Adresse anhand des
    /// festgelegten Anbieterprofils.
    ///
    /// Nur eindeutig interpretierte Ergebnisse
    /// dürfen Listed oder NotListed liefern.
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
                BlacklistId = _blacklistId,
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
                    "Das Prüfziel ist keine gültige IPv4-Adresse.");
        }

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutSource.CancelAfter(QueryTimeout);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            /*
             * 1. Positive Kontrollabfrage.
             *
             * Mindestens ein echter Listungscode
             * muss vorhanden sein.
             *
             * Alle übrigen Antworten müssen
             * ausdrücklich als zulässige
             * Kontrollcodes bekannt sein.
             *
             * Unbekannte Antworten bleiben Fehler.
             */
            var positiveQuery =
                BuildQueryName(
                    _positiveControlAddress,
                    _dnsZone);

            var positiveResponse =
                await _resolver.QueryAsync(
                    positiveQuery,
                    timeoutSource.Token);

            if (ContainsAccessDeniedCode(
                    positiveResponse))
            {
                return CreateResult(
                    BlacklistCheckStatus.AccessDenied,
                    errorMessage:
                        "Die positive Kontrollabfrage " +
                        "meldet einen gesperrten Zugriff.");
            }

            if (positiveResponse.IsNxDomain
                || positiveResponse.Addresses.Count == 0
                || !positiveResponse.Addresses.Any(
                    _listedCodes.Contains)
                || positiveResponse.Addresses.Any(
                    address =>
                        !_positiveControlCodes.Contains(
                            address)))
            {
                return CreateResult(
                    BlacklistCheckStatus.Failed,
                    errorMessage:
                        "Die positive Kontrollabfrage " +
                        "lieferte kein gültiges Ergebnis.");
            }

            /*
             * 2. Negative Kontrollabfrage.
             *
             * Die konfigurierte Negativadresse
             * muss NXDOMAIN liefern.
             *
             * Andernfalls ist eine zuverlässige
             * Negativbewertung nicht möglich.
             */
            var negativeQuery =
                BuildQueryName(
                    _negativeControlAddress,
                    _dnsZone);

            var negativeResponse =
                await _resolver.QueryAsync(
                    negativeQuery,
                    timeoutSource.Token);

            if (ContainsAccessDeniedCode(
                    negativeResponse))
            {
                return CreateResult(
                    BlacklistCheckStatus.AccessDenied,
                    errorMessage:
                        "Die negative Kontrollabfrage " +
                        "meldet einen gesperrten Zugriff.");
            }

            if (!negativeResponse.IsNxDomain)
            {
                return CreateResult(
                    BlacklistCheckStatus.Failed,
                    errorMessage:
                        "Die negative Kontrollabfrage " +
                        "lieferte kein NXDOMAIN.");
            }

            /*
             * 3. Tatsächliche Zielprüfung.
             *
             * Erst nach beiden erfolgreichen
             * Kontrollabfragen wird die
             * eigentliche Versand-IP abgefragt.
             */
            var targetQuery =
                BuildQueryName(
                    targetAddress,
                    _dnsZone);

            var targetResponse =
                await _resolver.QueryAsync(
                    targetQuery,
                    timeoutSource.Token);

            if (targetResponse.IsNxDomain)
            {
                return CreateResult(
                    BlacklistCheckStatus.NotListed,
                    providerResponse: "NXDOMAIN");
            }

            var responseText =
                string.Join(
                    ", ",
                    targetResponse.Addresses
                        .Select(
                            address => address.ToString()));

            /*
             * Zugriffssperren haben Vorrang.
             *
             * Sie dürfen niemals als
             * bestätigte Listung gelten.
             */
            if (ContainsAccessDeniedCode(
                    targetResponse))
            {
                return CreateResult(
                    BlacklistCheckStatus.AccessDenied,
                    providerResponse: responseText,
                    errorMessage:
                        "Der DNSBL-Anbieter meldet " +
                        "einen nicht erlaubten Zugriff.");
            }

            /*
             * Ausschließlich echte, freigegebene
             * Listungscodes dürfen Listed ergeben.
             *
             * Zusätzliche Kontrollcodes sind
             * hier ausdrücklich NICHT gültig.
             */
            if (targetResponse.Addresses.Count > 0
                && targetResponse.Addresses.All(
                    _listedCodes.Contains))
            {
                return CreateResult(
                    BlacklistCheckStatus.Listed,
                    providerResponse: responseText);
            }

            /*
             * Eine andere bekannte Reputationsstufe
             * ist nicht automatisch eine Listung.
             *
             * Unbekannte oder gemischte Antworten
             * werden nicht als NotListed oder
             * Listed ausgegeben.
             */
            return CreateResult(
                BlacklistCheckStatus.Unknown,
                providerResponse: responseText,
                errorMessage:
                    "Der Anbieter lieferte unbekannte " +
                    "oder für eine Blacklist-Listung " +
                    "nicht eindeutige DNS-Antworten.");
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return CreateResult(
                BlacklistCheckStatus.Failed,
                errorMessage:
                    "Die DNSBL-Prüfung hat das " +
                    "Zeitlimit von 8 Sekunden überschritten.");
        }
        catch (OperationCanceledException)
        {
            /*
             * Ein ausdrücklicher Abbruch durch
             * den aufrufenden Dienst bleibt erhalten.
             */
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
                    "DNSBL-Prüfung fehlgeschlagen: " +
                    exception.Message);
        }
    }

    private bool ContainsAccessDeniedCode(
        DnsBlacklistDnsResponse response)
    {
        return response.Addresses.Any(
            _accessDeniedCodes.Contains);
    }

    /// <summary>
    /// Erzeugt den DNSBL-Abfragenamen
    /// aus einer umgekehrten IPv4-Adresse.
    ///
    /// 192.0.2.10 wird zu:
    /// 10.2.0.192.example.test
    /// </summary>
    private static string BuildQueryName(
        IPAddress address,
        string dnsZone)
    {
        var bytes = address.GetAddressBytes();

        return
            $"{bytes[3]}." +
            $"{bytes[2]}." +
            $"{bytes[1]}." +
            $"{bytes[0]}." +
            dnsZone;
    }

    private static IPAddress ParseIpv4(
        string value,
        string description)
    {
        if (!IPAddress.TryParse(
                value,
                out var address)
            || address.AddressFamily
                != AddressFamily.InterNetwork)
        {
            throw new ArgumentException(
                $"Ungültige {description}: {value}");
        }

        return address;
    }

    private static HashSet<IPAddress> ParseReturnCodes(
        IReadOnlyCollection<string> codes,
        string description,
        bool requireNonEmpty)
    {
        ArgumentNullException.ThrowIfNull(codes);

        if (requireNonEmpty && codes.Count == 0)
        {
            throw new ArgumentException(
                $"Es wurden keine {description} angegeben.");
        }

        var parsedCodes =
            new HashSet<IPAddress>();

        foreach (var code in codes)
        {
            var address =
                ParseIpv4(
                    code,
                    description);

            /*
             * Nur klassische DNSBL-Rückgabecodes
             * aus dem Bereich 127.0.0.0/8
             * sind in dieser Engine zulässig.
             */
            if (address.GetAddressBytes()[0] != 127)
            {
                throw new ArgumentException(
                    $"Der {description}-Wert {code} " +
                    "liegt nicht in 127.0.0.0/8.");
            }

            if (!parsedCodes.Add(address))
            {
                throw new ArgumentException(
                    $"Doppelter {description}-Wert: {code}");
            }
        }

        return parsedCodes;
    }

    /// <summary>
    /// Strukturelle Prüfung einer DNS-Zone.
    ///
    /// Dies bestätigt ausdrücklich keine
    /// Zugangsberechtigung oder Erreichbarkeit.
    /// </summary>
    private static bool IsValidDnsZone(
        string dnsZone)
    {
        if (dnsZone.Length == 0
            || dnsZone.Length > 253)
        {
            return false;
        }

        var labels =
            dnsZone.Split('.');

        if (labels.Length < 2)
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (label.Length == 0
                || label.Length > 63
                || label[0] == '-'
                || label[^1] == '-')
            {
                return false;
            }

            if (label.Any(character =>
                    !char.IsAsciiLetterOrDigit(character)
                    && character != '-'))
            {
                return false;
            }
        }

        return true;
    }
}
