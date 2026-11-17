namespace BifitReport.LogViewer.Configuration;

public sealed class ViewerAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string DatabasePath { get; set; } = @"C:\ProgramData\BifitReport.LogViewer\identity.db";

    public string DataProtectionKeysPath { get; set; } = @"C:\ProgramData\BifitReport.LogViewer\keys";

    public int SessionHours { get; set; } = 8;
}
