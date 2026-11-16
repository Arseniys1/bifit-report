using BifitReport.LogViewer.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BifitReport.LogViewer.Infrastructure;

public sealed class LogSourcesHealthCheck(IOptions<LogViewerOptions> options) : IHealthCheck
{
    private readonly LogViewerOptions _options = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var missing = _options.Sources
            .Where(source => !Directory.Exists(Path.GetFullPath(Environment.ExpandEnvironmentVariables(source.RootPath))))
            .Select(source => source.Id)
            .ToArray();
        if (missing.Length == 0) return Task.FromResult(HealthCheckResult.Healthy());
        if (missing.Length < _options.Sources.Count)
        {
            return Task.FromResult(HealthCheckResult.Degraded($"Unavailable sources: {string.Join(", ", missing)}"));
        }
        return Task.FromResult(HealthCheckResult.Unhealthy("All configured log sources are unavailable."));
    }
}
