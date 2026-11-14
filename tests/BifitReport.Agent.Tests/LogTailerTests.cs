using System.Text;
using BifitReport.Agent.Configuration;
using BifitReport.Agent.Infrastructure;
using BifitReport.Agent.Tailing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Tests;

public sealed class LogTailerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "bifit-report-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadsOnlyCompleteLinesAndContinuesFromCheckpoint()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var logPath = Path.Combine(_temporaryDirectory, "desktop.log");
        await File.WriteAllTextAsync(logPath, "first\nsecond", Encoding.UTF8);

        var agent = Options.Create(new AgentOptions
        {
            LogPath = logPath,
            StateDirectory = Path.Combine(_temporaryDirectory, "state"),
            StartFromEndOnFirstRun = false
        });
        var delivery = Options.Create(new DeliveryOptions());
        var json = new AtomicJsonFile();
        var paths = new AppPaths(agent, delivery);
        var tailer = new LogTailer(agent, json, paths, NullLogger<LogTailer>.Instance);

        var firstBatch = await tailer.ReadAsync(CancellationToken.None);
        Assert.Single(firstBatch.Records);
        Assert.Equal("first", firstBatch.Records[0].Text.TrimStart('\uFEFF'));
        await tailer.CommitAsync(firstBatch, CancellationToken.None);

        await File.AppendAllTextAsync(logPath, " line\nthird\n", Encoding.UTF8);
        var secondBatch = await tailer.ReadAsync(CancellationToken.None);
        Assert.Equal(2, secondBatch.Records.Count);
        Assert.Equal("second line", secondBatch.Records[0].Text);
        Assert.Equal("third", secondBatch.Records[1].Text);
    }

    [Fact]
    public async Task FirstRunCanStartAtEnd()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var logPath = Path.Combine(_temporaryDirectory, "desktop.log");
        await File.WriteAllTextAsync(logPath, "historical\n", Encoding.UTF8);

        var agent = Options.Create(new AgentOptions
        {
            LogPath = logPath,
            StateDirectory = Path.Combine(_temporaryDirectory, "state"),
            StartFromEndOnFirstRun = true
        });
        var json = new AtomicJsonFile();
        var paths = new AppPaths(agent, Options.Create(new DeliveryOptions()));
        var tailer = new LogTailer(agent, json, paths, NullLogger<LogTailer>.Instance);

        Assert.Empty((await tailer.ReadAsync(CancellationToken.None)).Records);
        await File.AppendAllTextAsync(logPath, "live\n", Encoding.UTF8);
        var batch = await tailer.ReadAsync(CancellationToken.None);
        Assert.Single(batch.Records);
        Assert.Equal("live", batch.Records[0].Text);
    }

    [Fact]
    public async Task UncommittedBatchIsReturnedAgain()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var logPath = Path.Combine(_temporaryDirectory, "desktop.log");
        await File.WriteAllTextAsync(logPath, "retry-me\n", Encoding.UTF8);
        var agent = Options.Create(new AgentOptions
        {
            LogPath = logPath,
            StateDirectory = Path.Combine(_temporaryDirectory, "state"),
            StartFromEndOnFirstRun = false
        });
        var json = new AtomicJsonFile();
        var paths = new AppPaths(agent, Options.Create(new DeliveryOptions()));
        var tailer = new LogTailer(agent, json, paths, NullLogger<LogTailer>.Instance);

        var firstBatch = await tailer.ReadAsync(CancellationToken.None);
        var retryBatch = await tailer.ReadAsync(CancellationToken.None);

        Assert.Single(firstBatch.Records);
        Assert.Single(retryBatch.Records);
        Assert.Equal(firstBatch.Records[0], retryBatch.Records[0]);
    }

    [Fact]
    public async Task LogCreatedAfterAgentStartHonorsStartAtEnd()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var logPath = Path.Combine(_temporaryDirectory, "desktop.log");
        var agent = Options.Create(new AgentOptions
        {
            LogPath = logPath,
            StateDirectory = Path.Combine(_temporaryDirectory, "state"),
            StartFromEndOnFirstRun = true
        });
        var json = new AtomicJsonFile();
        var paths = new AppPaths(agent, Options.Create(new DeliveryOptions()));
        var tailer = new LogTailer(agent, json, paths, NullLogger<LogTailer>.Instance);

        Assert.False((await tailer.ReadAsync(CancellationToken.None)).LogExists);
        await File.WriteAllTextAsync(logPath, "historical\n", Encoding.UTF8);
        var firstExistingBatch = await tailer.ReadAsync(CancellationToken.None);

        Assert.Empty(firstExistingBatch.Records);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
