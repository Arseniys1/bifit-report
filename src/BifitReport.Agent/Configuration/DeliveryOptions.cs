namespace BifitReport.Agent.Configuration;

public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    public string Mode { get; set; } = "File";

    public string PreviewFileName { get; set; } = "delivery-preview.log";

    public int SentMarkerRetentionDays { get; set; } = 90;

    public bool IsVkMode => string.Equals(Mode, "VK", StringComparison.OrdinalIgnoreCase);

    public bool IsFileMode => string.Equals(Mode, "File", StringComparison.OrdinalIgnoreCase);
}
