using BifitReport.Agent.Configuration;
using BifitReport.Agent.Delivery;
using BifitReport.Agent.Infrastructure;
using BifitReport.Agent.Queue;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Workers;

public sealed class OutboundDeliveryWorker(
    IOptions<DeliveryOptions> deliveryOptions,
    IOptions<VkOptions> vkOptions,
    DiskMessageQueue queue,
    VkApiClient vkApiClient,
    AgentHealthStore healthStore,
    ILogger<OutboundDeliveryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);

    private readonly DeliveryOptions _deliveryOptions = deliveryOptions.Value;
    private readonly VkOptions _vkOptions = vkOptions.Value;
    private DateTimeOffset _lastCleanupAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CleanupMarkers();
        logger.LogInformation("Доставка отчётов запущена в режиме {DeliveryMode}.", _deliveryOptions.Mode);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.Now - _lastCleanupAt >= CleanupInterval) CleanupMarkers();

                var message = await queue.GetNextReadyAsync(DateTimeOffset.Now, stoppingToken);
                if (message is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                if (_deliveryOptions.IsFileMode)
                {
                    if (!message.PreviewDelivered)
                    {
                        await queue.AppendPreviewAsync(message, stoppingToken);
                        message.PreviewDelivered = true;
                    }

                    await queue.CompleteAsync(message, "File", stoppingToken);
                    await RecordDeliveryAsync("File", true, null, stoppingToken);
                    continue;
                }

                await DeliverToVkAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Ошибка обработчика очереди; сообщение останется на диске для повтора.");
                await RecordWorkerErrorAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private void CleanupMarkers()
    {
        _lastCleanupAt = DateTimeOffset.Now;
        queue.CleanupSentMarkers();
    }

    private async Task DeliverToVkAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        if (_vkOptions.PeerIds.Count == 0)
        {
            await ScheduleRetryAsync(message, "no_peer_ids", retryable: false, cancellationToken);
            return;
        }

        foreach (var peerId in _vkOptions.PeerIds.Distinct())
        {
            if (message.DeliveredPeerIds.ContainsKey(peerId)) continue;

            var result = await vkApiClient.SendAsync(message.Id, peerId, message.Text, cancellationToken);
            if (!result.Success)
            {
                await ScheduleRetryAsync(message, result.ErrorCode ?? "vk_error", result.Retryable, cancellationToken);
                return;
            }

            message.DeliveredPeerIds[peerId] = DateTimeOffset.Now;
            await queue.UpdateAsync(message, cancellationToken);
        }

        await queue.CompleteAsync(message, "VK", cancellationToken);
        await RecordDeliveryAsync("VK", true, null, cancellationToken);
        logger.LogInformation("Отчёт {MessageId} доставлен в VK всем получателям.", message.Id);
    }

    private async Task ScheduleRetryAsync(
        OutboundMessage message,
        string errorCode,
        bool retryable,
        CancellationToken cancellationToken)
    {
        message.Attempts++;
        message.LastErrorCode = errorCode;

        if (!retryable && message.Attempts >= _vkOptions.DeadLetterAttempts)
        {
            await queue.FailAsync(message, errorCode, cancellationToken);
            await RecordDeliveryAsync("VK", false, errorCode, cancellationToken);
            return;
        }

        var maximumMinutes = _vkOptions.MaximumRetryDelayMinutes;
        var delayMinutes = retryable
            ? Math.Min(maximumMinutes, Math.Pow(2, Math.Min(message.Attempts, 10)) / 4d)
            : maximumMinutes;
        message.NextAttemptAt = DateTimeOffset.Now.AddMinutes(Math.Max(1, delayMinutes));
        await queue.UpdateAsync(message, cancellationToken);
        await RecordDeliveryAsync("VK", false, errorCode, cancellationToken);
        logger.LogWarning(
            "Отчёт {MessageId} не доставлен ({ErrorCode}); следующая попытка {NextAttemptAt}.",
            message.Id,
            errorCode,
            message.NextAttemptAt);
    }

    private async Task RecordDeliveryAsync(string mode, bool success, string? errorCode, CancellationToken cancellationToken)
    {
        try
        {
            await healthStore.RecordDeliveryAsync(mode, success, errorCode, cancellationToken);
        }
        catch (Exception exception)
        {
            // Запись статуса не должна останавливать доставку.
            logger.LogWarning(exception, "Не удалось обновить статус доставки.");
        }
    }

    private async Task RecordWorkerErrorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await healthStore.RecordWorkerErrorAsync(nameof(OutboundDeliveryWorker), cancellationToken);
        }
        catch (Exception exception)
        {
            // Ошибка в обработчике ошибки не должна валить весь воркер и службу.
            logger.LogWarning(exception, "Не удалось зафиксировать ошибку воркера доставки.");
        }
    }
}
