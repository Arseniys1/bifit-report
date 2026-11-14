namespace BifitReport.Agent.Infrastructure;

public sealed class AgentHealthStore(AtomicJsonFile jsonFile, AppPaths paths)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AgentHealthStatus _status = new();

    public async Task RecordLogPollAsync(long position, int records, CancellationToken cancellationToken)
    {
        await UpdateAsync(status =>
        {
            status.LastLogPollAt = DateTimeOffset.Now;
            status.LogPosition = position;
            if (records > 0)
            {
                status.LastLogActivityAt = DateTimeOffset.Now;
                status.LastBatchRecordCount = records;
            }
        }, cancellationToken);
    }

    public Task RecordDeliveryAsync(string mode, bool success, string? errorCode, CancellationToken cancellationToken) =>
        UpdateAsync(status =>
        {
            status.LastDeliveryAttemptAt = DateTimeOffset.Now;
            status.DeliveryMode = mode;
            status.LastDeliverySucceeded = success;
            status.LastDeliveryErrorCode = errorCode;
            if (success) status.LastSuccessfulDeliveryAt = DateTimeOffset.Now;
        }, cancellationToken);

    public Task RecordWorkerErrorAsync(string worker, CancellationToken cancellationToken) =>
        UpdateAsync(status =>
        {
            status.LastWorkerErrorAt = DateTimeOffset.Now;
            status.LastWorkerWithError = worker;
        }, cancellationToken);

    private async Task UpdateAsync(Action<AgentHealthStatus> update, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            update(_status);
            _status.ProcessId = Environment.ProcessId;
            _status.UpdatedAt = DateTimeOffset.Now;
            await jsonFile.WriteAsync(paths.HealthStatusPath, _status, cancellationToken);
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
