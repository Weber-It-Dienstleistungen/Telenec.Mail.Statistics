using System.Text.RegularExpressions;
using Telenec.Mail.Statistics.Server.Contracts;

namespace Telenec.Mail.Statistics.Server.Validation;

public sealed class UsageRequestValidator : IUsageRequestValidator
{
    private static readonly Regex ApplicationVersionPattern = new(
        @"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?(?:\+[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public bool TryValidate(
        HeartbeatRequest? request,
        out ValidatedHeartbeatRequest? validatedRequest,
        out string? error)
    {
        validatedRequest = null;

        if (request is null)
        {
            error = "Der Request darf nicht leer sein.";
            return false;
        }

        if (!TryValidateInstallationId(
                request.InstallationId,
                out var installationId,
                out error))
        {
            return false;
        }

        if (string.IsNullOrEmpty(request.ApplicationVersion))
        {
            error = "Die Anwendungsversion fehlt.";
            return false;
        }

        if (!string.Equals(
                request.ApplicationVersion,
                request.ApplicationVersion.Trim(),
                StringComparison.Ordinal))
        {
            error = "Die Anwendungsversion enthält unzulässige Leerzeichen.";
            return false;
        }

        if (request.ApplicationVersion.Length >
            UsageStatisticsProtocol.MaximumApplicationVersionLength)
        {
            error = "Die Anwendungsversion ist zu lang.";
            return false;
        }

        if (!ApplicationVersionPattern.IsMatch(request.ApplicationVersion))
        {
            error = "Die Anwendungsversion hat ein ungültiges Format.";
            return false;
        }

        if (request.ConsentVersion !=
            UsageStatisticsProtocol.CurrentConsentVersion)
        {
            error = "Die Einwilligungsversion wird nicht unterstützt.";
            return false;
        }

        validatedRequest = new ValidatedHeartbeatRequest(
            installationId,
            request.ApplicationVersion,
            request.ConsentVersion);

        error = null;
        return true;
    }

    public bool TryValidate(
        RevokeRequest? request,
        out ValidatedRevokeRequest? validatedRequest,
        out string? error)
    {
        validatedRequest = null;

        if (request is null)
        {
            error = "Der Request darf nicht leer sein.";
            return false;
        }

        if (!TryValidateInstallationId(
                request.InstallationId,
                out var installationId,
                out error))
        {
            return false;
        }

        validatedRequest = new ValidatedRevokeRequest(installationId);

        error = null;
        return true;
    }

    private static bool TryValidateInstallationId(
        string? value,
        out Guid installationId,
        out string? error)
    {
        installationId = Guid.Empty;

        if (string.IsNullOrEmpty(value))
        {
            error = "Die Installations-ID fehlt.";
            return false;
        }

        if (value.Length != 36)
        {
            error = "Die Installations-ID hat ein ungültiges Format.";
            return false;
        }

        if (!Guid.TryParseExact(value, "D", out installationId))
        {
            error = "Die Installations-ID hat ein ungültiges Format.";
            return false;
        }

        if (installationId == Guid.Empty)
        {
            error = "Die Installations-ID darf nicht leer sein.";
            return false;
        }

        error = null;
        return true;
    }
}