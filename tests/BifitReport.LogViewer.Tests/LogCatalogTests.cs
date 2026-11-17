using BifitReport.LogViewer.Configuration;
using BifitReport.LogViewer.Logs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BifitReport.LogViewer.Tests;

public sealed class LogCatalogTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "bifit-log-viewer-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ListsOnlyAllowedFilesAndKeepsPhysicalPathPrivate()
    {
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "archive"));
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "desktop.log"), "line\n");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "secret.txt"), "secret\n");
        await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, "archive", "desktop-1.log"), "line\n");
        var catalog = CreateCatalog();

        var page = catalog.GetFiles("bifit", 1, 100, null);

        Assert.Equal(2, page.Total);
        Assert.All(page.Items, file => Assert.DoesNotContain(_temporaryDirectory, file.RelativePath, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(page.Items, file => file.Name == "secret.txt");
    }

    [Fact]
    public async Task RejectsTraversalEvenWhenTokenIsWellFormed()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var outsidePath = Path.Combine(Path.GetDirectoryName(_temporaryDirectory)!, "outside.log");
        await File.WriteAllTextAsync(outsidePath, "must not be served\n");
        try
        {
            var catalog = CreateCatalog();
            var token = FileToken.Encode(Path.Combine("..", "outside.log"));

            Assert.Throws<LogResourceNotFoundException>(() => catalog.ResolveFile("bifit", token));
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    [Fact]
    public async Task PaginatesEntireCatalogAndClampsOutOfRangePage()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        for (var index = 0; index < 205; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(_temporaryDirectory, $"desktop-{index:D3}.log"), "line\n");
        }
        var catalog = CreateCatalog();

        var secondPage = catalog.GetFiles("bifit", 2, 200, null);
        var outOfRangePage = catalog.GetFiles("bifit", int.MaxValue, 200, null);

        Assert.Equal(205, secondPage.Total);
        Assert.Equal(5, secondPage.Items.Count);
        Assert.Equal(2, outOfRangePage.Page);
        Assert.Equal(5, outOfRangePage.Items.Count);
    }

    private LogCatalog CreateCatalog() => new(
        Options.Create(TestOptions.Create(_temporaryDirectory)),
        NullLogger<LogCatalog>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
