using BifitReport.Agent.Configuration;
using BifitReport.Agent.Infrastructure;
using BifitReport.Agent.Parsing;
using BifitReport.Agent.State;
using BifitReport.Agent.Tailing;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Workers;

public sealed class LogIngestionWorker(
    IOptions<AgentOptions> options,
    LogTailer tailer,
    BifitLogParser parser,
    ReportAggregator aggregator,
    AgentHealthStore healthStore,
    ILogger<LogIngestionWorker> logger) : BackgroundService
{
    private readonly AgentOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Наблюдение за журналом БИФИТ запущено. Файл: {LogPath}", _options.LogPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await tailer.ReadAsync(stoppingToken);
                foreach (var record in batch.Records)
                {
                    var bifitEvent = parser.Parse(record);
                    if (bifitEvent is not null) await aggregator.HandleAsync(bifitEvent, stoppingToken);
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
                await healthStore.RecordWorkerErrorAsync(nameof(LogIngestionWorker), stoppingToken);
            }

            await Task.Delay(_options.PollIntervalMilliseconds, stoppingToken);
        }
    }
}
