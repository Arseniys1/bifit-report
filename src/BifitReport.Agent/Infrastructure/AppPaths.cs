using BifitReport.Agent.Configuration;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Infrastructure;

public sealed class AppPaths
{
    public AppPaths(IOptions<AgentOptions> agentOptions, IOptions<DeliveryOptions> deliveryOptions)
    {
        StateDirectory = Normalize(agentOptions.Value.StateDirectory);
        QueueDirectory = Path.Combine(StateDirectory, "queue");
        PendingQueueDirectory = Path.Combine(QueueDirectory, "pending");
        SentQueueDirectory = Path.Combine(QueueDirectory, "sent");
        FailedQueueDirectory = Path.Combine(QueueDirectory, "failed");
        TailCheckpointPath = Path.Combine(StateDirectory, "tail-checkpoint.json");
        ReportStatePath = Path.Combine(StateDirectory, "report-state.json");
        HealthStatusPath = Path.Combine(StateDirectory, "status.json");
        ArchiveStatusPath = Path.Combine(StateDirectory, "archive-status.json");
        PreviewFilePath = Path.Combine(StateDirectory, deliveryOptions.Value.PreviewFileName);

        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(PendingQueueDirectory);
        Directory.CreateDirectory(SentQueueDirectory);
        Directory.CreateDirectory(FailedQueueDirectory);
    }

    public string StateDirectory { get; }

    public string QueueDirectory { get; }

    public string PendingQueueDirectory { get; }

    public string SentQueueDirectory { get; }

    public string FailedQueueDirectory { get; }

    public string TailCheckpointPath { get; }

    public string ReportStatePath { get; }

    public string HealthStatusPath { get; }

    public string ArchiveStatusPath { get; }

    public string PreviewFilePath { get; }

    public static string Normalize(string path) =>
        Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
}
