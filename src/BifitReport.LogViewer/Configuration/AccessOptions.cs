namespace BifitReport.LogViewer.Configuration;

public sealed class AccessOptions
{
    public const string SectionName = "Access";

    public List<string> AllowedPrincipals { get; set; } = ["S-1-5-32-544"];
}
