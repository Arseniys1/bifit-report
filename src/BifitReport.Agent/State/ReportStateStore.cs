using BifitReport.Agent.Infrastructure;

namespace BifitReport.Agent.State;

public sealed class ReportStateStore(
    AtomicJsonFile jsonFile,
    AppPaths paths,
    ILogger<ReportStateStore> logger)
{
    public async Task<ReportState> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await jsonFile.ReadAsync<ReportState>(paths.ReportStatePath, cancellationToken) ?? new ReportState();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            logger.LogWarning(exception, "Состояние отчёта не удалось прочитать; начат новый период накопления.");
            return new ReportState();
        }
    }

    public Task SaveAsync(ReportState state, CancellationToken cancellationToken) =>
        jsonFile.WriteAsync(paths.ReportStatePath, state, cancellationToken);
}

