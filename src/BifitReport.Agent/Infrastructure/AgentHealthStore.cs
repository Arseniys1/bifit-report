namespace BifitReport.Agent.Infrastructure;

public sealed class AgentHealthStore(
    AtomicJsonFile jsonFile,
    AppPaths paths,
    ILogger<AgentHealthStore> logger)
{
    /// <summary>
    /// Опрос журнала обновляет статус каждые две секунды, но записывать файл так часто
    /// незачем: это лишний износ диска. Значимые события (доставка, ошибка) пишутся сразу.
    /// </summary>
    private static readonly TimeSpan MinimumPollWriteInterval = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private AgentHealthStatus _status = new();
    private DateTimeOffset _lastWriteAt = DateTimeOffset.MinValue;

    public Task RecordLogPollAsync(long position, int records, CancellationToken cancellationToken) =>
        UpdateAsync(status =>
        {
            status.LastLogPollAt = DateTimeOffset.Now;
            status.LogPosition = position;
            if (records > 0)
            {
                status.LastLogActivityAt = DateTimeOffset.Now;
                status.LastBatchRecordCount = records;
            }
        }, forceWrite: false, cancellationToken);

    public Task RecordDeliveryAsync(string mode, bool success, string? errorCode, CancellationToken cancellationToken) =>
        UpdateAsync(status =>
        {
            status.LastDeliveryAttemptAt = DateTimeOffset.Now;
            status.DeliveryMode = mode;
            status.LastDeliverySucceeded = success;
            status.LastDeliveryErrorCode = errorCode;
            if (success) status.LastSuccessfulDeliveryAt = DateTimeOffset.Now;
        }, forceWrite: true, cancellationToken);

    public Task RecordWorkerErrorAsync(string worker, CancellationToken cancellationToken) =>
        UpdateAsync(status =>
        {
            status.LastWorkerErrorAt = DateTimeOffset.Now;
            status.LastWorkerWithError = worker;
        }, forceWrite: true, cancellationToken);

    private async Task UpdateAsync(
        Action<AgentHealthStatus> update,
        bool forceWrite,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            update(_status);

            var now = DateTimeOffset.Now;
            if (!forceWrite && now - _lastWriteAt < MinimumPollWriteInterval) return;

            _status.ProcessId = Environment.ProcessId;
            _status.UpdatedAt = now;

            // Отметку времени ставим до записи: недоступный каталог не должен
            // приводить к попытке писать файл на каждом опросе.
            _lastWriteAt = now;

            try
            {
                await jsonFile.WriteAsync(paths.HealthStatusPath, _status, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Запись статуса — диагностика, а не работа агента. Недоступный каталог
                // состояния не должен останавливать наблюдение за журналом и доставку.
                logger.LogWarning(exception, "Не удалось обновить {Path}; статус остаётся в памяти.", paths.HealthStatusPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class AgentHealthStatus
{
    public DateTimeOffset UpdatedAt { get; set; }
    public int ProcessId { get; set; }
    public DateTimeOffset? LastLogPollAt { get; set; }
    public DateTimeOffset? LastLogActivityAt { get; set; }
    public long LogPosition { get; set; }
    public int LastBatchRecordCount { get; set; }
    public string? DeliveryMode { get; set; }
    public DateTimeOffset? LastDeliveryAttemptAt { get; set; }
    public DateTimeOffset? LastSuccessfulDeliveryAt { get; set; }
    public bool? LastDeliverySucceeded { get; set; }
    public string? LastDeliveryErrorCode { get; set; }
    public DateTimeOffset? LastWorkerErrorAt { get; set; }
    public string? LastWorkerWithError { get; set; }
}
