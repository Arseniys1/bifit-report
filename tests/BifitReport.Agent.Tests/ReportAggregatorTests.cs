using BifitReport.Agent.Configuration;
using BifitReport.Agent.Infrastructure;
using BifitReport.Agent.Models;
using BifitReport.Agent.Queue;
using BifitReport.Agent.Reporting;
using BifitReport.Agent.State;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Tests;

public sealed class ReportAggregatorTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "bifit-report-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ErrorOutsideShiftDoesNotCreatePhantomShift()
    {
        var context = CreateContext();
        var bifitEvent = new BifitEvent(
            "error-1",
            BifitEventType.ApplicationError,
            DateTimeOffset.Now,
            "SomeLogger",
            ErrorCategory: "Прочая ошибка",
            IsCritical: false,
            SafeMessage: "safe");

        await context.Aggregator.HandleAsync(bifitEvent, CancellationToken.None);
        var state = await context.JsonFile.ReadAsync<ReportState>(context.Paths.ReportStatePath, CancellationToken.None);

        Assert.NotNull(state);
        Assert.Null(state.CurrentShift);
    }

    [Fact]
    public async Task QueueDeduplicatesPendingAndDeliveredMessages()
    {
        var context = CreateContext();

        Assert.True(await context.Queue.EnqueueAsync("message", "same-key", CancellationToken.None));
        Assert.False(await context.Queue.EnqueueAsync("message", "same-key", CancellationToken.None));

        var queued = await context.Queue.GetNextReadyAsync(DateTimeOffset.Now.AddSeconds(1), CancellationToken.None);
        Assert.NotNull(queued);
        await context.Queue.CompleteAsync(queued, "File", CancellationToken.None);

        Assert.False(await context.Queue.EnqueueAsync("message", "same-key", CancellationToken.None));
    }

    [Fact]
    public void InferredShiftReportIsExplicitlyMarkedAsIncomplete()
    {
        var formatter = new ReportFormatter(Options.Create(new ReportOptions()));
        var text = formatter.FormatShiftClosed(new ShiftReportState
        {
            InternalId = "4",
            OpenedAt = DateTimeOffset.Now.AddHours(-1),
            ClosedAt = DateTimeOffset.Now,
            IsInferred = true
        });

        Assert.Contains("Данные неполные", text, StringComparison.Ordinal);
    }

    private TestContext CreateContext()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var agentOptions = Options.Create(new AgentOptions
        {
            StateDirectory = _temporaryDirectory
        });
        var deliveryOptions = Options.Create(new DeliveryOptions());
        var reportOptions = Options.Create(new ReportOptions());
        var jsonFile = new AtomicJsonFile();
        var paths = new AppPaths(agentOptions, deliveryOptions);
        var queue = new DiskMessageQueue(jsonFile, paths, deliveryOptions, NullLogger<DiskMessageQueue>.Instance);
        var stateStore = new ReportStateStore(jsonFile, paths, NullLogger<ReportStateStore>.Instance);
        var formatter = new ReportFormatter(reportOptions);
        var aggregator = new ReportAggregator(
            agentOptions,
            reportOptions,
            stateStore,
            formatter,
            queue,
            NullLogger<ReportAggregator>.Instance);
        return new TestContext(aggregator, queue, jsonFile, paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private sealed record TestContext(
        ReportAggregator Aggregator,
        DiskMessageQueue Queue,
        AtomicJsonFile JsonFile,
        AppPaths Paths);
}
