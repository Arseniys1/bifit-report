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

        var tailer = CreateTailer(logPath, startFromEnd: false);
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

        var tailer = CreateTailer(logPath, startFromEnd: true);
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
        var tailer = CreateTailer(logPath, startFromEnd: false);

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
        var tailer = CreateTailer(logPath, startFromEnd: true);

        Assert.False((await tailer.ReadAsync(CancellationToken.None)).LogExists);
        await File.WriteAllTextAsync(logPath, "historical\n", Encoding.UTF8);
        var firstExistingBatch = await tailer.ReadAsync(CancellationToken.None);

        Assert.Empty(firstExistingBatch.Records);
    }

    [Fact]
    public async Task DetectsRotationWhenCreationTimeIsPreservedAndFileIsLonger()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var logPath = Path.Combine(_temporaryDirectory, "desktop.log");
        await File.WriteAllTextAsync(logPath, "first\n", Encoding.UTF8);

        var tailer = CreateTailer(logPath, startFromEnd: false);
        var firstBatch = await tailer.ReadAsync(CancellationToken.None);
        await tailer.CommitAsync(firstBatch, CancellationToken.None);

        // NTFS сохраняет время создания при пересоздании файла с тем же именем
        // (file system tunneling), поэтому длина файла тоже не помогает: новый файл длиннее.
        var creationTime = File.GetCreationTimeUtc(logPath);
        File.Move(logPath, Path.Combine(_temporaryDirectory, "desktop-1.log"));
        await File.WriteAllTextAsync(logPath, "second\nthird\nfourth\n", Encoding.UTF8);
        File.SetCreationTimeUtc(logPath, creationTime);

        try
        {
            var batch = await tailer.ReadAsync(CancellationToken.None);
            Assert.Equal(["second", "third", "fourth"], batch.Records.Select(record => record.Text));
        }
        finally
        {
            File.SetCreationTimeUtc(logPath, DateTime.UtcNow);
        }
    }

    [Fact]
    public async Task DetectsInPlaceTruncationAndRegrowBeforeNextPoll()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var logPath = Path.Combine(_temporaryDirectory, "desktop.log");
        await File.WriteAllTextAsync(logPath, "aaaa\nbbbb\n", Encoding.UTF8);

        var tailer = CreateTailer(logPath, startFromEnd: false);
        var firstBatch = await tailer.ReadAsync(CancellationToken.None);
        await tailer.CommitAsync(firstBatch, CancellationToken.None);
        Assert.Equal(new FileInfo(logPath).Length, firstBatch.Checkpoint.Position);

        // Журнал переписан на месте и уже длиннее сохранённой позиции.
        await File.WriteAllTextAsync(logPath, "cccc\ndddd\neeee\n", Encoding.UTF8);

        var batch = await tailer.ReadAsync(CancellationToken.None);

        Assert.Equal(["cccc", "dddd", "eeee"], batch.Records.Select(record => record.Text));
    }

    [Fact]
    public async Task CheckpointIsNotRewrittenWhenNothingChanged()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var logPath = Path.Combine(_temporaryDirectory, "desktop.log");
        await File.WriteAllTextAsync(logPath, "only\n", Encoding.UTF8);

        var agentOptions = CreateOptions(logPath, startFromEnd: false);
        var json = new AtomicJsonFile();
        var paths = new AppPaths(agentOptions, Options.Create(new DeliveryOptions()));
        var tailer = new LogTailer(agentOptions, json, paths, NullLogger<LogTailer>.Instance);

        var firstBatch = await tailer.ReadAsync(CancellationToken.None);
        await tailer.CommitAsync(firstBatch, CancellationToken.None);
        var savedAfterFirstCommit = await json.ReadAsync<TailCheckpoint>(paths.TailCheckpointPath, CancellationToken.None);
        Assert.NotNull(savedAfterFirstCommit);

        await Task.Delay(50);
        var idleBatch = await tailer.ReadAsync(CancellationToken.None);
        await tailer.CommitAsync(idleBatch, CancellationToken.None);
        var savedAfterIdlePoll = await json.ReadAsync<TailCheckpoint>(paths.TailCheckpointPath, CancellationToken.None);

        Assert.Equal(savedAfterFirstCommit.UpdatedAt, savedAfterIdlePoll!.UpdatedAt);
        Assert.Equal(savedAfterFirstCommit.Position, savedAfterIdlePoll.Position);
    }

    private LogTailer CreateTailer(string logPath, bool startFromEnd)
    {
        var agentOptions = CreateOptions(logPath, startFromEnd);
        var json = new AtomicJsonFile();
        var paths = new AppPaths(agentOptions, Options.Create(new DeliveryOptions()));
        return new LogTailer(agentOptions, json, paths, NullLogger<LogTailer>.Instance);
    }

    private IOptions<AgentOptions> CreateOptions(string logPath, bool startFromEnd) =>
        Options.Create(new AgentOptions
        {
            LogPath = logPath,
            StateDirectory = Path.Combine(_temporaryDirectory, "state"),
            StartFromEndOnFirstRun = startFromEnd
        });

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
