using System.Security.Cryptography;
using System.Text;
using BifitReport.Agent.Configuration;
using BifitReport.Agent.Infrastructure;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Queue;

public sealed class DiskMessageQueue(
    AtomicJsonFile jsonFile,
    AppPaths paths,
    IOptions<DeliveryOptions> deliveryOptions,
    ILogger<DiskMessageQueue> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DeliveryOptions _deliveryOptions = deliveryOptions.Value;

    public async Task<bool> EnqueueAsync(string text, string deduplicationKey, CancellationToken cancellationToken)
    {
        var id = Hash(deduplicationKey);
        var pendingPath = PendingPath(id);
        var sentPath = SentPath(id);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(pendingPath) || File.Exists(sentPath)) return false;

            var message = new OutboundMessage
            {
                Id = id,
                DeduplicationKey = deduplicationKey,
                Text = text,
                CreatedAt = DateTimeOffset.Now,
                NextAttemptAt = DateTimeOffset.Now
            };
            await jsonFile.WriteAsync(pendingPath, message, cancellationToken);
            logger.LogInformation("Отчёт поставлен в очередь: {MessageId}", id);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OutboundMessage?> GetNextReadyAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Файлы очереди названы хешами, поэтому порядок чтения должен задаваться временем
        // создания отчёта, иначе сообщения об открытии и закрытии смены приходят вразнобой.
        var ready = new List<OutboundMessage>();
        foreach (var path in Directory.EnumerateFiles(paths.PendingQueueDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var message = await jsonFile.ReadAsync<OutboundMessage>(path, cancellationToken);
                if (message is not null && message.NextAttemptAt <= now) ready.Add(message);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                logger.LogWarning(exception, "Не удалось прочитать элемент очереди {QueueFile}", Path.GetFileName(path));
            }
        }

        return ready
            .OrderBy(static message => message.CreatedAt)
            .ThenBy(static message => message.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public Task UpdateAsync(OutboundMessage message, CancellationToken cancellationToken) =>
        jsonFile.WriteAsync(PendingPath(message.Id), message, cancellationToken);

    public async Task CompleteAsync(OutboundMessage message, string mode, CancellationToken cancellationToken)
    {
        var marker = new SentMarker
        {
            Id = message.Id,
            DeduplicationKey = message.DeduplicationKey,
            Mode = mode,
            DeliveredAt = DateTimeOffset.Now
        };

        await jsonFile.WriteAsync(SentPath(message.Id), marker, cancellationToken);
        var pendingPath = PendingPath(message.Id);
        if (File.Exists(pendingPath)) File.Delete(pendingPath);
    }

    public async Task AppendPreviewAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PreviewFilePath)!);
        var separator = new string('-', 72);
        var content = $"{Environment.NewLine}{separator}{Environment.NewLine}{DateTimeOffset.Now:O} | {message.Id}{Environment.NewLine}{message.Text}{Environment.NewLine}";
        await File.AppendAllTextAsync(paths.PreviewFilePath, content, Encoding.UTF8, cancellationToken);
    }

    public void CleanupSentMarkers()
    {
        var threshold = DateTime.UtcNow.AddDays(-_deliveryOptions.SentMarkerRetentionDays);
        foreach (var path in Directory.EnumerateFiles(paths.SentQueueDirectory, "*.json"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < threshold) File.Delete(path);
            }
            catch (IOException exception)
            {
                logger.LogDebug(exception, "Не удалось удалить старый маркер доставки {Marker}", Path.GetFileName(path));
            }
        }
    }

    private string PendingPath(string id) => Path.Combine(paths.PendingQueueDirectory, id + ".json");

    private string SentPath(string id) => Path.Combine(paths.SentQueueDirectory, id + ".json");

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private sealed class SentMarker
    {
        public string Id { get; set; } = string.Empty;
        public string DeduplicationKey { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public DateTimeOffset DeliveredAt { get; set; }
    }
}
