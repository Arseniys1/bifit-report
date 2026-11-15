namespace BifitReport.Agent.State;

public sealed class ArchiveStatus
{
    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    public int SourceCount { get; set; }

    public int Copied { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public string? LastError { get; set; }
}
