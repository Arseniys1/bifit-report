namespace BifitReport.Agent.State;

public sealed class ReportState
{
    public ShiftReportState? CurrentShift { get; set; }

    public List<string> RecentEventIds { get; set; } = [];

    public Dictionary<string, DateTimeOffset> LastCriticalNotificationByCategory { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset? LastPeriodicSummaryAt { get; set; }
}

public sealed class ShiftReportState
{
    public string InternalId { get; set; } = "не определена";

    public string ExternalId { get; set; } = "не определена";

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public bool IsInferred { get; set; }

    public HashSet<string> RegisteredReceiptIds { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> DeletedReceiptIds { get; set; } = new(StringComparer.Ordinal);

    public int TerminalSuccessCount { get; set; }

    public int TerminalFailureCount { get; set; }

    public decimal RecognizedTerminalSuccessAmount { get; set; }

    public decimal? PendingTerminalAmount { get; set; }

    public DateTimeOffset? PendingTerminalStartedAt { get; set; }

    public int ErrorCount { get; set; }

    public int CriticalErrorCount { get; set; }

    public Dictionary<string, int> ErrorCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

