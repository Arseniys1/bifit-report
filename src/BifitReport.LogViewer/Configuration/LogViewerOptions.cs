namespace BifitReport.LogViewer.Configuration;

public sealed class LogViewerOptions
{
    public const string SectionName = "LogViewer";

    public List<LogSourceOptions> Sources { get; set; } = [];

    public int DefaultTailLines { get; set; } = 500;

    public int MaximumTailLines { get; set; } = 2_000;

    public int MaximumCatalogFiles { get; set; } = 5_000;

    public int MaximumScanBytes { get; set; } = 8 * 1024 * 1024;

    public int MaximumLiveReadBytes { get; set; } = 1024 * 1024;

    public int MaximumLineCharacters { get; set; } = 256 * 1024;

    public long MaximumCompressedFileBytes { get; set; } = 128L * 1024 * 1024;

    public long MaximumDecompressedFileBytes { get; set; } = 512L * 1024 * 1024;

    public bool AllowRawLogs { get; set; }
}

public sealed class LogSourceOptions
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    public List<string> Includes { get; set; } = ["**/*.log", "**/*.log.gz"];

    public bool Recursive { get; set; } = true;
}
