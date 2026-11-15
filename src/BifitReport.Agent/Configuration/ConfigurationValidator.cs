using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Configuration;

public sealed class ConfigurationValidator(IConfiguration configuration) :
    IValidateOptions<AgentOptions>,
    IValidateOptions<DeliveryOptions>,
    IValidateOptions<VkOptions>,
    IValidateOptions<ReportOptions>,
    IValidateOptions<ArchiveOptions>
{
    public ValidateOptionsResult Validate(string? name, AgentOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.LogPath)) failures.Add("Agent:LogPath не задан.");
        if (string.IsNullOrWhiteSpace(options.StateDirectory)) failures.Add("Agent:StateDirectory не задан.");
        if (options.PollIntervalMilliseconds is < 250 or > 60_000) failures.Add("Agent:PollIntervalMilliseconds должен быть от 250 до 60000.");
        if (options.MaxBytesPerPoll is < 4_096 or > 16_777_216) failures.Add("Agent:MaxBytesPerPoll должен быть от 4096 до 16777216.");
        if (options.MaxLineBytes is < 1_024 or > 4_194_304) failures.Add("Agent:MaxLineBytes должен быть от 1024 до 4194304.");
        if (options.RecentEventCapacity is < 100 or > 100_000) failures.Add("Agent:RecentEventCapacity должен быть от 100 до 100000.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    public ValidateOptionsResult Validate(string? name, DeliveryOptions options)
    {
        var failures = new List<string>();
        if (!options.IsFileMode && !options.IsVkMode) failures.Add("Delivery:Mode должен быть File или VK.");
        if (string.IsNullOrWhiteSpace(options.PreviewFileName)) failures.Add("Delivery:PreviewFileName не задан.");
        if (options.SentMarkerRetentionDays is < 1 or > 3650) failures.Add("Delivery:SentMarkerRetentionDays должен быть от 1 до 3650.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    public ValidateOptionsResult Validate(string? name, VkOptions options)
    {
        var failures = new List<string>();
        if (!Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("Vk:ApiBaseUrl должен быть абсолютным HTTPS-адресом.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiVersion)) failures.Add("Vk:ApiVersion не задан.");
        if (options.PeerIds.Any(static id => id <= 0)) failures.Add("Vk:PeerIds не должен содержать нулевые или отрицательные значения.");
        if (options.PeerIds.Count == 0 && IsVkDeliveryConfigured())
        {
            failures.Add("Vk:PeerIds должен содержать хотя бы одного получателя при Delivery:Mode = VK.");
        }

        if (options.TimeoutSeconds is < 2 or > 120) failures.Add("Vk:TimeoutSeconds должен быть от 2 до 120.");
        if (options.MaximumRetryDelayMinutes is < 1 or > 1440) failures.Add("Vk:MaximumRetryDelayMinutes должен быть от 1 до 1440.");
        if (options.DeadLetterAttempts is < 1 or > 100) failures.Add("Vk:DeadLetterAttempts должен быть от 1 до 100.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    public ValidateOptionsResult Validate(string? name, ReportOptions options)
    {
        var failures = new List<string>();
        if (options.CriticalErrorCooldownMinutes is < 1 or > 1440) failures.Add("Reports:CriticalErrorCooldownMinutes должен быть от 1 до 1440.");
        if (options.PeriodicSummaryMinutes is < 5 or > 1440) failures.Add("Reports:PeriodicSummaryMinutes должен быть от 5 до 1440.");
        if (options.PendingPaymentLifetimeMinutes is < 1 or > 120) failures.Add("Reports:PendingPaymentLifetimeMinutes должен быть от 1 до 120.");
        if (options.MaxExceptionLines is < 0 or > 50) failures.Add("Reports:MaxExceptionLines должен быть от 0 до 50.");
        if (options.MaxExceptionCharacters is < 100 or > 3000) failures.Add("Reports:MaxExceptionCharacters должен быть от 100 до 3000.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    public ValidateOptionsResult Validate(string? name, ArchiveOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Destination))
        {
            failures.Add("Archive:Destination не задан.");
        }

        if (options.Sources.Count == 0)
        {
            failures.Add("Archive:Sources должен содержать хотя бы один каталог журналов.");
        }

        if (options.Patterns.Count == 0 || options.Patterns.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("Archive:Patterns должен содержать хотя бы одну непустую маску файла.");
        }

        if (options.MinFileAgeMinutes is < 0 or > 1440)
        {
            failures.Add("Archive:MinFileAgeMinutes должен быть от 0 до 1440.");
        }

        if (options.PollIntervalMinutes is < 1 or > 1440)
        {
            failures.Add("Archive:PollIntervalMinutes должен быть от 1 до 1440.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedSources = new List<(string Name, string Path)>();
        foreach (var source in options.Sources)
        {
            var sourceName = source.Name?.Trim() ?? string.Empty;
            if (sourceName.Length == 0)
            {
                failures.Add("Archive:Sources:Name не задан.");
            }
            else if (sourceName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                failures.Add($"Archive:Sources:Name «{sourceName}» содержит недопустимые для имени каталога символы.");
            }
            else if (!names.Add(sourceName))
            {
                failures.Add($"Archive:Sources содержит повторяющееся имя «{sourceName}».");
            }

            if (string.IsNullOrWhiteSpace(source.Path))
            {
                failures.Add($"Archive:Sources:Path не задан для «{sourceName}».");
            }
            else if (TryNormalizePath(source.Path, out var sourcePath))
            {
                normalizedSources.Add((sourceName, sourcePath));
            }
            else
            {
                failures.Add($"Archive:Sources:Path «{source.Path}» имеет недопустимый формат.");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.Destination) &&
            TryNormalizePath(options.Destination, out var destination))
        {
            foreach (var source in normalizedSources)
            {
                if (IsSameOrInside(destination, source.Path))
                {
                    failures.Add($"Archive:Destination не должен находиться внутри источника «{source.Name}».");
                }
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private bool IsVkDeliveryConfigured() =>
        string.Equals(configuration["Delivery:Mode"], "VK", StringComparison.OrdinalIgnoreCase);

    private static bool TryNormalizePath(string path, out string normalized)
    {
        try
        {
            normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static bool IsSameOrInside(string candidate, string parent)
    {
        var parentPath = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
        var candidatePath = Path.TrimEndingDirectorySeparator(candidate) + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase);
    }
}
