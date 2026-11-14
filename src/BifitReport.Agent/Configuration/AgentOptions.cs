namespace BifitReport.Agent.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string LogPath { get; set; } = @"C:\ProgramData\BIFIT\retail\logs\desktop.log";

    public string StateDirectory { get; set; } = @"C:\ProgramData\BifitReport";

    public int PollIntervalMilliseconds { get; set; } = 2_000;

    public bool StartFromEndOnFirstRun { get; set; } = true;

    public int MaxBytesPerPoll { get; set; } = 1_048_576;

    public int MaxLineBytes { get; set; } = 262_144;

    public int RecentEventCapacity { get; set; } = 5_000;
}
