using BifitReport.Agent.Configuration;
using BifitReport.Agent.Infrastructure;
using BifitReport.Agent.Parsing;
using BifitReport.Agent.State;
using BifitReport.Agent.Tailing;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Workers;

public sealed class LogIngestionWorker(
    IOptions<AgentOptions> options,
    IOptions<ReportOptions> reportOptions,
    LogTailer tailer,
    BifitLogParser parser,
    ReportAggregator aggregator,
    AgentHealthStore healthStore,
    ILogger<LogIngestionWorker> logger) : BackgroundService
{
    private readonly AgentOptions _options = options.Value;
    private readonly ReportOptions _reportOptions = reportOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Наблюдение за журналом БИФИТ запущено. Файл: {LogPath}", _options.LogPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await tailer.ReadAsync(stoppingToken);
                foreach (var bifitEvent in parser.ParseBatch(
                             batch.Records,
                             _reportOptions.MaxExceptionLines,
                             _reportOptions.MaxExceptionCharacters))
                {
                    await aggregator.HandleAsync(bifitEvent, stoppingToken);
                }

                if (batch.LogExists) await tailer.CommitAsync(batch, stoppingToken);
                await healthStore.RecordLogPollAsync(batch.Checkpoint.Position, batch.Records.Count, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Ошибка наблюдателя журнала; кассовое приложение не затронуто, чтение будет повторено.");
                await RecordWorkerErrorAsync(stoppingToken);
            }

            await Task.Delay(_options.PollIntervalMilliseconds, stoppingToken);
        }
    }

    private async Task RecordWorkerErrorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await healthStore.RecordWorkerErrorAsync(nameof(LogIngestionWorker), cancellationToken);
        }
        catch (Exception exception)
        {
            // Ошибка в обработчике ошибки не должна валить весь воркер и службу.
            logger.LogWarning(exception, "Не удалось зафиксировать ошибку наблюдателя журнала.");
        }
    }
}
