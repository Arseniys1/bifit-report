namespace BifitReport.Agent.Configuration;

public sealed class ReportOptions
{
    public const string SectionName = "Reports";

    public string StoreName { get; set; } = string.Empty;

    public bool SendShiftOpened { get; set; } = true;

    public bool SendShiftClosed { get; set; } = true;

    public bool SendImmediateCriticalErrors { get; set; } = true;

    public bool IncludeSanitizedErrorMessage { get; set; }

    public int CriticalErrorCooldownMinutes { get; set; } = 10;

    public bool PeriodicSummaryEnabled { get; set; }

    public int PeriodicSummaryMinutes { get; set; } = 60;

    public int PendingPaymentLifetimeMinutes { get; set; } = 10;
}
