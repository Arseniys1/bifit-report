namespace BifitReport.Agent.Queue;

public sealed class OutboundMessage
{
    public string Id { get; set; } = string.Empty;

    public string DeduplicationKey { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public int Attempts { get; set; }

    public Dictionary<long, DateTimeOffset> DeliveredPeerIds { get; set; } = [];

    public bool PreviewDelivered { get; set; }

    public string? LastErrorCode { get; set; }
}
