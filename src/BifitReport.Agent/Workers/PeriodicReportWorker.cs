using BifitReport.Agent.State;

namespace BifitReport.Agent.Workers;

public sealed class PeriodicReportWorker(
    ReportAggregator aggregator,
    ILogger<PeriodicReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await aggregator.TrySendPeriodicSummaryAsync(DateTimeOffset.Now, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Не удалось сформировать периодический отчёт.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
