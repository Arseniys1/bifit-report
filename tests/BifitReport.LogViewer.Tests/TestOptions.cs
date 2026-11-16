using BifitReport.LogViewer.Configuration;

namespace BifitReport.LogViewer.Tests;

internal static class TestOptions
{
    public static LogViewerOptions Create(string rootPath) => new()
    {
        Sources =
        [
            new LogSourceOptions
            {
                Id = "bifit",
                DisplayName = "БИФИТ",
                RootPath = rootPath,
                Includes = ["**/*.log", "**/*.log.gz"],
                Recursive = true
            }
        ],
        DefaultTailLines = 100,
        MaximumTailLines = 2_000,
        MaximumCatalogFiles = 1_000,
        MaximumScanBytes = 1024 * 1024,
        MaximumLiveReadBytes = 64 * 1024,
        MaximumLineCharacters = 64 * 1024,
        MaximumCompressedFileBytes = 16 * 1024 * 1024,
        MaximumDecompressedFileBytes = 64 * 1024 * 1024,
        AllowRawLogs = false
    };
}
