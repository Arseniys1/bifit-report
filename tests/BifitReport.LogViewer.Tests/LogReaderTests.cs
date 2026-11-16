using System.IO.Compression;
using System.Text;
using BifitReport.Core.Security;
using BifitReport.LogViewer.Logs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BifitReport.LogViewer.Tests;

public sealed class LogReaderTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "bifit-log-viewer-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadsTailFiltersAndRedactsSensitiveData()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, "desktop.log");
        await File.WriteAllTextAsync(path, string.Join('\n', new[]
        {
            "2026-11-17 10:00:00,000 INFO logger [thread] ignored",
            "2026-11-17 10:00:01,000 ERROR logger [thread] access_token=vk1.secret",
            "2026-11-17 10:00:02,000 ERROR logger [thread] second"
        }) + "\n", new UTF8Encoding(false));
        var reader = CreateReader();

        var content = await reader.ReadTailAsync(Resolve(path), 10, null, "ERROR", raw: false, CancellationToken.None);

        Assert.Equal(2, content.Lines.Count);
        Assert.DoesNotContain("vk1.secret", content.Lines[0], StringComparison.Ordinal);
        Assert.Contains("[СКРЫТО]", content.Lines[0], StringComparison.Ordinal);
        Assert.True(content.Redacted);
        Assert.True(content.CanFollow);
        Assert.NotNull(content.Cursor);
    }

    [Fact]
    public async Task LiveTailReturnsOnlyCompleteAppendedLines()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, "desktop.log");
        await File.WriteAllTextAsync(path, "first\n", new UTF8Encoding(false));
        var reader = CreateReader();
        var initial = await reader.ReadTailAsync(Resolve(path), 10, null, null, raw: false, CancellationToken.None);

        await File.AppendAllTextAsync(path, "second", new UTF8Encoding(false));
        var incomplete = await reader.ReadUpdatesAsync(Resolve(path), initial.Cursor!.Value, initial.Revision!, null, null, raw: false, CancellationToken.None);
        await File.AppendAllTextAsync(path, " line\n", new UTF8Encoding(false));
        var complete = await reader.ReadUpdatesAsync(Resolve(path), incomplete.Cursor, incomplete.Revision, null, null, raw: false, CancellationToken.None);

        Assert.Empty(incomplete.Lines);
        Assert.Equal(initial.Cursor, incomplete.Cursor);
        Assert.Equal(["second line"], complete.Lines);
    }

    [Fact]
    public async Task LiveTailRequestsResetAfterFileReplacement()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, "desktop.log");
        await File.WriteAllTextAsync(path, "old first line\nold second line\n", new UTF8Encoding(false));
        var reader = CreateReader();
        var initial = await reader.ReadTailAsync(Resolve(path), 10, null, null, raw: false, CancellationToken.None);

        await File.WriteAllTextAsync(path, "new\n", new UTF8Encoding(false));
        var update = await reader.ReadUpdatesAsync(Resolve(path), initial.Cursor!.Value, initial.Revision!, null, null, raw: false, CancellationToken.None);

        Assert.True(update.Reset);
        Assert.Empty(update.Lines);
    }

    [Fact]
    public async Task ReadsLastMatchingLinesFromGzipArchive()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, "desktop-1.log.gz");
        await using (var file = File.Create(path))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        await using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            await writer.WriteAsync(
                "2026-11-17 10:00:00,000 INFO logger [thread] one\n" +
                "2026-11-17 10:00:01,000 ERROR logger [thread] two\n" +
                "2026-11-17 10:00:02,000 ERROR logger [thread] three\n");
        }
        var reader = CreateReader();

        var content = await reader.ReadTailAsync(Resolve(path), 1, null, "ERROR", raw: false, CancellationToken.None);

        Assert.Single(content.Lines);
        Assert.EndsWith("ERROR logger [thread] three", content.Lines[0], StringComparison.Ordinal);
        Assert.False(content.CanFollow);
        Assert.Null(content.Cursor);
    }

    [Fact]
    public async Task BoundsVeryLongLinesInGzipArchive()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, "desktop-oversized.log.gz");
        await using (var file = File.Create(path))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        await using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        {
            await writer.WriteLineAsync(new string('x', 80 * 1024));
        }
        var reader = CreateReader();

        var content = await reader.ReadTailAsync(Resolve(path), 1, null, null, raw: false, CancellationToken.None);

        Assert.Single(content.Lines);
        Assert.Equal(64 * 1024 + 1, content.Lines[0].Length);
        Assert.EndsWith("…", content.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RawAccessIsDeniedUnlessExplicitlyEnabled()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, "desktop.log");
        await File.WriteAllTextAsync(path, "line\n");
        var reader = CreateReader();

        await Assert.ThrowsAsync<LogResourceNotFoundException>(() =>
            reader.ReadTailAsync(Resolve(path), 10, null, null, raw: true, CancellationToken.None));
    }

    private LogReader CreateReader() => new(
        Options.Create(TestOptions.Create(_temporaryDirectory)),
        new SensitiveDataRedactor(),
        NullLogger<LogReader>.Instance);

    private ResolvedLogFile Resolve(string path) => new(
        new LogSourceOptionsSnapshot("bifit", "БИФИТ", _temporaryDirectory, ["**/*.log", "**/*.log.gz"], true),
        path,
        Path.GetRelativePath(_temporaryDirectory, path),
        path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
