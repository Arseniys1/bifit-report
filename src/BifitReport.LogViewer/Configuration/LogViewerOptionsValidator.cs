using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace BifitReport.LogViewer.Configuration;

public sealed partial class LogViewerOptionsValidator :
    IValidateOptions<LogViewerOptions>,
    IValidateOptions<ViewerAuthenticationOptions>,
    IValidateOptions<ServerOptions>
{
    public ValidateOptionsResult Validate(string? name, LogViewerOptions options)
    {
        var errors = new List<string>();
        if (options.Sources.Count == 0) errors.Add("LogViewer:Sources must contain at least one source.");
        if (options.DefaultTailLines is < 1 or > 2_000) errors.Add("LogViewer:DefaultTailLines must be between 1 and 2000.");
        if (options.MaximumTailLines is < 1 or > 10_000) errors.Add("LogViewer:MaximumTailLines must be between 1 and 10000.");
        if (options.DefaultTailLines > options.MaximumTailLines) errors.Add("DefaultTailLines cannot exceed MaximumTailLines.");
        if (options.MaximumCatalogFiles is < 1 or > 100_000) errors.Add("MaximumCatalogFiles must be between 1 and 100000.");
        if (options.MaximumScanBytes is < 65_536 or > 256 * 1024 * 1024) errors.Add("MaximumScanBytes is outside the safe range.");
        if (options.MaximumLiveReadBytes is < 4_096 or > 16 * 1024 * 1024) errors.Add("MaximumLiveReadBytes is outside the safe range.");
        if (options.MaximumLineCharacters is < 1_024 or > 2 * 1024 * 1024) errors.Add("MaximumLineCharacters is outside the safe range.");
        if (options.MaximumCompressedFileBytes is < 1_024 or > 2L * 1024 * 1024 * 1024) errors.Add("MaximumCompressedFileBytes is outside the safe range.");
        if (options.MaximumDecompressedFileBytes < options.MaximumCompressedFileBytes) errors.Add("MaximumDecompressedFileBytes must not be smaller than MaximumCompressedFileBytes.");

        var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in options.Sources)
        {
            if (!SourceIdRegex().IsMatch(source.Id)) errors.Add($"Invalid source id '{source.Id}'.");
            if (!sourceIds.Add(source.Id)) errors.Add($"Duplicate source id '{source.Id}'.");
            if (string.IsNullOrWhiteSpace(source.DisplayName)) errors.Add($"Source '{source.Id}' has no display name.");
            if (string.IsNullOrWhiteSpace(source.RootPath) || !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(source.RootPath)))
            {
                errors.Add($"Source '{source.Id}' must have an absolute RootPath.");
            }
            if (source.Includes.Count == 0 || source.Includes.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"Source '{source.Id}' must contain at least one non-empty include pattern.");
            }
            if (source.Includes.Any(pattern => pattern.Contains("..", StringComparison.Ordinal)))
            {
                errors.Add($"Source '{source.Id}' contains an unsafe include pattern.");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public ValidateOptionsResult Validate(string? name, ViewerAuthenticationOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.DatabasePath) ||
            !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(options.DatabasePath)))
        {
            errors.Add("Authentication:DatabasePath must be an absolute path.");
        }
        if (string.IsNullOrWhiteSpace(options.DataProtectionKeysPath) ||
            !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(options.DataProtectionKeysPath)))
        {
            errors.Add("Authentication:DataProtectionKeysPath must be an absolute path.");
        }
        if (options.SessionHours is < 1 or > 168)
        {
            errors.Add("Authentication:SessionHours must be between 1 and 168.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public ValidateOptionsResult Validate(string? name, ServerOptions options)
    {
        if (!Uri.TryCreate(options.ListenUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return ValidateOptionsResult.Fail("Server:ListenUrl must be an absolute HTTP or HTTPS origin without a path.");
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            return ValidateOptionsResult.Fail("Plain HTTP is allowed only on a loopback address. Configure HTTPS for remote access.");
        }

        if (uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrWhiteSpace(options.CertificateThumbprint))
        {
            return ValidateOptionsResult.Fail("Server:CertificateThumbprint is required for HTTPS.");
        }

        return ValidateOptionsResult.Success;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceIdRegex();
}
