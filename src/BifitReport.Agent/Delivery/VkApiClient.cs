using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BifitReport.Agent.Configuration;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Delivery;

public sealed class VkApiClient : IDisposable
{
    /// <summary>VK принимает не более 4096 символов; оставляем небольшой запас.</summary>
    private const int MaximumTextLength = 4_000;

    private readonly VkOptions _options;
    private readonly VkTokenProvider _tokenProvider;
    private readonly ILogger<VkApiClient> _logger;
    private readonly HttpClient _httpClient;

    public VkApiClient(
        IOptions<VkOptions> options,
        VkTokenProvider tokenProvider,
        ILogger<VkApiClient> logger)
    {
        _options = options.Value;
        _tokenProvider = tokenProvider;
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.NormalizedApiBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds)
        };
    }

    public async Task<VkSendResult> SendAsync(
        string messageId,
        long peerId,
        string text,
        CancellationToken cancellationToken)
    {
        try
        {
            var messageText = FitToVkLimit(messageId, text);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["access_token"] = _tokenProvider.GetToken(),
                ["v"] = _options.ApiVersion,
                ["peer_id"] = peerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["random_id"] = CreateRandomId(messageId, peerId).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["message"] = messageText
            });

            using var response = await _httpClient.PostAsync("messages.send", content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;

                // 4xx (кроме 408/429) — это ошибка запроса или доступа, повторять её
                // каждый час бессмысленно: пусть сработает ограничение попыток.
                var retryable = statusCode is 408 or 429 || statusCode >= 500;
                _logger.LogWarning("VK API вернул HTTP {StatusCode} для получателя {PeerId}.", statusCode, peerId);
                return new VkSendResult(false, retryable, $"http_{statusCode}");
            }

            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("response", out _))
            {
                return new VkSendResult(true, false, null);
            }

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("error_code", out var codeElement) ? codeElement.GetInt32() : -1;
                var retryable = code is 1 or 6 or 9 or 10 or 29;
                _logger.LogWarning("VK API отклонил сообщение для {PeerId}, код {ErrorCode}.", peerId, code);
                return new VkSendResult(false, retryable, $"vk_{code}");
            }

            return new VkSendResult(false, true, "vk_unexpected_response");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new VkSendResult(false, true, "timeout");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Сетевая ошибка при отправке сообщения в VK.");
            return new VkSendResult(false, true, "network");
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "VK API вернул ответ неизвестного формата.");
            return new VkSendResult(false, true, "invalid_json");
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError("Отправка VK не настроена: {Reason}", exception.Message);
            return new VkSendResult(false, false, "configuration");
        }
        catch (CryptographicException exception)
        {
            _logger.LogError(exception, "Не удалось расшифровать токен VK.");
            return new VkSendResult(false, false, "token_decryption");
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private string FitToVkLimit(string messageId, string text)
    {
        if (text.Length <= MaximumTextLength) return text;

        var cut = MaximumTextLength - 1;
        // Не разрываем суррогатную пару: иначе последний символ станет невалидным.
        if (char.IsHighSurrogate(text[cut - 1])) cut--;

        _logger.LogWarning(
            "Текст отчёта {MessageId} сокращён с {OriginalLength} до {MaximumLength} символов из-за лимита VK.",
            messageId,
            text.Length,
            MaximumTextLength);
        return text[..cut] + "…";
    }

    private static int CreateRandomId(string messageId, long peerId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{messageId}:{peerId}"));
        var value = BitConverter.ToInt32(bytes, 0) & int.MaxValue;
        return value == 0 ? 1 : value;
    }
}

public sealed record VkSendResult(bool Success, bool Retryable, string? ErrorCode);
