namespace BifitReport.Agent.Models;

public enum BifitEventType
{
    ShiftOpened,
    ShiftClosed,
    ReceiptRegistered,
    ReceiptDeleted,
    TerminalPaymentStarted,
    TerminalPaymentCompleted,
    ApplicationError
}

public sealed record BifitEvent(
    string Id,
    BifitEventType Type,
    DateTimeOffset Timestamp,
    string Logger,
    string? ShiftInternalId = null,
    string? ShiftExternalId = null,
    DateTimeOffset? ShiftOpenedAt = null,
    DateTimeOffset? ShiftClosedAt = null,
    string? ReceiptId = null,
    decimal? Amount = null,
    bool? Success = null,
    string? ErrorCategory = null,
    bool IsCritical = false,
    string? SafeMessage = null);
