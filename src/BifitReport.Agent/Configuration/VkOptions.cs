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

    /// <summary>
    /// Базовый адрес с завершающим слешем: без него относительный путь метода
    /// разрешается от корня хоста и запрос уходит не на <c>/method/messages.send</c>.
    /// </summary>
    public string NormalizedApiBaseUrl =>
        string.IsNullOrWhiteSpace(ApiBaseUrl) ? ApiBaseUrl : ApiBaseUrl.TrimEnd('/') + "/";
}
