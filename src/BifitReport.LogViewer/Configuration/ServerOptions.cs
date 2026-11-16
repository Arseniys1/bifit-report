namespace BifitReport.LogViewer.Configuration;

public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public string ListenUrl { get; set; } = "http://127.0.0.1:5080";

    public string CertificateThumbprint { get; set; } = string.Empty;
}
