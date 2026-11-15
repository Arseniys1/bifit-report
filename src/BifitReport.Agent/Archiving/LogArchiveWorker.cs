using BifitReport.Agent.Configuration;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Archiving;

public sealed class LogArchiveWorker(
    LogArchiver archiver,
    IOptions<ArchiveOptions> options,
    ILogger<LogArchiveWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Архивирование журналов отключено (Archive:Enabled = false).");
            return;
        }

        logger.LogInformation(
            "Архивирование журналов запущено: источников {SourceCount}, каталог {Destination}, период {PollIntervalMinutes} мин.",
            options.Value.Sources.Count,
            options.Value.Destination,
            options.Value.PollIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await archiver.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Ошибка архивирования журналов; повтор при следующем цикле.");
            }

            await Task.Delay(TimeSpan.FromMinutes(options.Value.PollIntervalMinutes), stoppingToken);
        }
    }
}
