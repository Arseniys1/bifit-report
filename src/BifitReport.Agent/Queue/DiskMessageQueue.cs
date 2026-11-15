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
        var failedPath = FailedPath(id);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(pendingPath) || File.Exists(sentPath) || File.Exists(failedPath)) return false;

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
        string[] files;
        try
        {
            files = Directory.GetFiles(paths.PendingQueueDirectory, "*.json");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Недоступный каталог очереди не должен останавливать доставку навсегда:
            // следующая попытка будет через несколько секунд.
            logger.LogWarning(exception, "Каталог очереди недоступен: {Directory}", paths.PendingQueueDirectory);
            return null;
        }

        var ready = new List<OutboundMessage>();
        foreach (var path in files)
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

    public async Task UpdateAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await jsonFile.WriteAsync(PendingPath(message.Id), message, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CompleteAsync(OutboundMessage message, string mode, CancellationToken cancellationToken)
    {
        var marker = new SentMarker
        {
            Id = message.Id,
            DeduplicationKey = message.DeduplicationKey,
            Mode = mode,
            DeliveredAt = DateTimeOffset.Now
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await jsonFile.WriteAsync(SentPath(message.Id), marker, cancellationToken);
            var pendingPath = PendingPath(message.Id);
            if (File.Exists(pendingPath)) File.Delete(pendingPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Переносит сообщение в <c>queue\failed</c>: повторять его бессмысленно, но и терять
    /// текст отчёта нельзя — администратор должен увидеть, что именно не доставлено.
    /// </summary>
    public async Task FailAsync(OutboundMessage message, string reason, CancellationToken cancellationToken)
    {
        var marker = new FailedMarker
        {
            Id = message.Id,
            DeduplicationKey = message.DeduplicationKey,
            Reason = reason,
            Attempts = message.Attempts,
            CreatedAt = message.CreatedAt,
            FailedAt = DateTimeOffset.Now,
            Text = message.Text
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await jsonFile.WriteAsync(FailedPath(message.Id), marker, cancellationToken);
            var pendingPath = PendingPath(message.Id);
            if (File.Exists(pendingPath)) File.Delete(pendingPath);
            logger.LogError(
                "Отчёт {MessageId} снят с повторов после {Attempts} попыток ({Reason}); файл остался в {Directory}.",
                message.Id,
                message.Attempts,
                reason,
                paths.FailedQueueDirectory);
        }
        finally
        {
            _gate.Release();
        }
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
        CleanupDirectory(paths.SentQueueDirectory, threshold, "маркер доставки");
        CleanupDirectory(paths.FailedQueueDirectory, threshold, "файл недоставленного отчёта");
    }

    private void CleanupDirectory(string directory, DateTime threshold, string description)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.json");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "Каталог {Directory} недоступен для очистки.", directory);
            return;
        }

        foreach (var path in files)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < threshold) File.Delete(path);
            }
            catch (IOException exception)
            {
                logger.LogDebug(exception, "Не удалось удалить старый {Description} {Marker}", description, Path.GetFileName(path));
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogDebug(exception, "Нет доступа к старому {Description} {Marker}", description, Path.GetFileName(path));
            }
        }
    }

    private string PendingPath(string id) => Path.Combine(paths.PendingQueueDirectory, id + ".json");

    private string SentPath(string id) => Path.Combine(paths.SentQueueDirectory, id + ".json");

    private string FailedPath(string id) => Path.Combine(paths.FailedQueueDirectory, id + ".json");

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

    private sealed class FailedMarker
    {
        public string Id { get; set; } = string.Empty;
        public string DeduplicationKey { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset FailedAt { get; set; }
        public string Text { get; set; } = string.Empty;
    }
}
