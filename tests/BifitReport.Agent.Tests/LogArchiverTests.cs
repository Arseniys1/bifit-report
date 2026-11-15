using System.Text;
using BifitReport.Agent.Archiving;
using BifitReport.Agent.Configuration;
using BifitReport.Agent.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Tests;

public sealed class LogArchiverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bifit-archive-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CopiesRotatedLogsAndLeavesSourceIntact()
    {
        var sourceDirectory = Path.Combine(_root, "kassa", "logs");
        var destinationDirectory = Path.Combine(_root, "archive");
        Directory.CreateDirectory(sourceDirectory);

        var rotated = CreateFile(sourceDirectory, "desktop-01-01-2026-1.log.gz", "rotated desktop", DateTime.UtcNow.AddHours(-2));
        var chzn = CreateFile(sourceDirectory, "chzn-01-01-2026-1.log.gz", "rotated chzn", DateTime.UtcNow.AddHours(-2));
        var fresh = CreateFile(sourceDirectory, "desktop-01-02-2026-1.log.gz", "still being written", DateTime.UtcNow);
        CreateFile(sourceDirectory, "desktop.log", "live log", DateTime.UtcNow.AddHours(-2));
        CreateFile(sourceDirectory, "unrelated.txt", "not a log", DateTime.UtcNow.AddHours(-2));

        var context = CreateContext(sourceDirectory, destinationDirectory);
        var status = await context.Archiver.RunAsync(CancellationToken.None);

        Assert.Equal(2, status.Copied);
        Assert.Equal(1, status.Skipped);
        Assert.Equal(0, status.Failed);
        Assert.Equal("rotated desktop", await File.ReadAllTextAsync(Path.Combine(destinationDirectory, "kassa", "desktop-01-01-2026-1.log.gz")));
        Assert.Equal("rotated chzn", await File.ReadAllTextAsync(Path.Combine(destinationDirectory, "kassa", "chzn-01-01-2026-1.log.gz")));
        Assert.False(File.Exists(Path.Combine(destinationDirectory, "kassa", "desktop.log")));
        Assert.False(File.Exists(Path.Combine(destinationDirectory, "kassa", "unrelated.txt")));
        Assert.True(File.Exists(rotated));
        Assert.True(File.Exists(chzn));
        Assert.True(File.Exists(fresh));

        var secondStatus = await context.Archiver.RunAsync(CancellationToken.None);
        Assert.Equal(0, secondStatus.Copied);
        Assert.Equal(3, secondStatus.Skipped);
    }

    [Fact]
    public async Task CopiesChangedFileAgain()
    {
        var sourceDirectory = Path.Combine(_root, "kassa", "logs");
        var destinationDirectory = Path.Combine(_root, "archive");
        Directory.CreateDirectory(sourceDirectory);
        var path = CreateFile(sourceDirectory, "desktop-01-01-2026-1.log.gz", "first", DateTime.UtcNow.AddHours(-2));

        var context = CreateContext(sourceDirectory, destinationDirectory);
        Assert.Equal(1, (await context.Archiver.RunAsync(CancellationToken.None)).Copied);

        await File.WriteAllTextAsync(path, "second", Encoding.UTF8);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));

        var status = await context.Archiver.RunAsync(CancellationToken.None);
        Assert.Equal(1, status.Copied);
        Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(destinationDirectory, "kassa", "desktop-01-01-2026-1.log.gz")));
    }

    [Fact]
    public async Task MissingSourceIsReportedWithoutThrowing()
    {
        var destinationDirectory = Path.Combine(_root, "archive");
        var missingSource = Path.Combine(_root, "missing", "logs");
        var context = CreateContext(missingSource, destinationDirectory);

        var status = await context.Archiver.RunAsync(CancellationToken.None);

        Assert.Equal(0, status.Copied);
        Assert.True(status.Failed >= 1);
        Assert.Contains("не найден", status.LastError, StringComparison.Ordinal);
    }

    private TestContext CreateContext(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(_root);
        var agentOptions = Options.Create(new AgentOptions
        {
            StateDirectory = Path.Combine(_root, "state")
        });
        var deliveryOptions = Options.Create(new DeliveryOptions());
        var archiveOptions = Options.Create(new ArchiveOptions
        {
            Enabled = true,
            Destination = destinationDirectory,
            Sources = [new ArchiveSource { Name = "kassa", Path = sourceDirectory }],
            MinFileAgeMinutes = 10
        });
        var jsonFile = new AtomicJsonFile();
        var paths = new AppPaths(agentOptions, deliveryOptions);
        var archiver = new LogArchiver(
            archiveOptions,
            jsonFile,
            paths,
            NullLogger<LogArchiver>.Instance);
        return new TestContext(archiver, paths);
    }

    private static string CreateFile(string directory, string name, string content, DateTime lastWriteUtc)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content, Encoding.UTF8);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed record TestContext(LogArchiver Archiver, AppPaths Paths);
}
