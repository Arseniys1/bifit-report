using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Configuration;

public sealed class ConfigurationValidator :
    IValidateOptions<AgentOptions>,
    IValidateOptions<DeliveryOptions>,
    IValidateOptions<VkOptions>,
    IValidateOptions<ReportOptions>
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
        if (options.PeerIds.Any(static id => id == 0)) failures.Add("Vk:PeerIds не должен содержать 0.");
        if (options.TimeoutSeconds is < 2 or > 120) failures.Add("Vk:TimeoutSeconds должен быть от 2 до 120.");
        if (options.MaximumRetryDelayMinutes is < 1 or > 1440) failures.Add("Vk:MaximumRetryDelayMinutes должен быть от 1 до 1440.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    public ValidateOptionsResult Validate(string? name, ReportOptions options)
    {
        var failures = new List<string>();
        if (options.CriticalErrorCooldownMinutes is < 1 or > 1440) failures.Add("Reports:CriticalErrorCooldownMinutes должен быть от 1 до 1440.");
        if (options.PeriodicSummaryMinutes is < 5 or > 1440) failures.Add("Reports:PeriodicSummaryMinutes должен быть от 5 до 1440.");
        if (options.PendingPaymentLifetimeMinutes is < 1 or > 120) failures.Add("Reports:PendingPaymentLifetimeMinutes должен быть от 1 до 120.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
