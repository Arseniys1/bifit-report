namespace BifitReport.Agent.Configuration;

public sealed class VkOptions
{
    public const string SectionName = "Vk";

    public string ApiBaseUrl { get; set; } = "https://api.vk.ru/method/";

    public string ApiVersion { get; set; } = "5.199";

    public List<long> PeerIds { get; set; } = [];

    public string TokenFile { get; set; } = @"C:\ProgramData\BifitReport\secrets\vk-token.dat";

    public int TimeoutSeconds { get; set; } = 15;

    public int MaximumRetryDelayMinutes { get; set; } = 60;
}
