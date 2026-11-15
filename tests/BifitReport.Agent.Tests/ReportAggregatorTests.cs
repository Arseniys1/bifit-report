using System.Globalization;
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
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T15:00:00+05:00", CultureInfo.InvariantCulture);

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
        var state = await ReadStateAsync(context);

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
    public async Task ReadyMessagesAreDeliveredInCreationOrder()
    {
        var context = CreateContext();
        string[] expected = ["first", "second", "third"];
        foreach (var text in expected)
        {
            Assert.True(await context.Queue.EnqueueAsync(text, text, CancellationToken.None));
            await Task.Delay(10);
        }

        var actual = new List<string>();
        foreach (var _ in expected)
        {
            var message = await context.Queue.GetNextReadyAsync(DateTimeOffset.Now.AddMinutes(1), CancellationToken.None);
            Assert.NotNull(message);
            actual.Add(message.Text);
            await context.Queue.CompleteAsync(message, "File", CancellationToken.None);
        }

        Assert.Equal(expected, actual);
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

    [Fact]
    public async Task CloseOfUnobservedShiftKeepsObservedCounters()
    {
        var context = CreateContext();

        // Агент поставлен в середине смены: открытия он не видел, но операции уже считает.
        await context.Aggregator.HandleAsync(
            new BifitEvent("receipt-1", BifitEventType.ReceiptRegistered, Now, "R", ReceiptId: "51665"),
            CancellationToken.None);
        await context.Aggregator.HandleAsync(
            new BifitEvent("payment-1", BifitEventType.TerminalPaymentStarted, Now.AddMinutes(1), "T", Amount: 100m),
            CancellationToken.None);
        await context.Aggregator.HandleAsync(
            new BifitEvent("payment-2", BifitEventType.TerminalPaymentCompleted, Now.AddMinutes(2), "T", Success: true),
            CancellationToken.None);

        await context.Aggregator.HandleAsync(
            new BifitEvent(
                "close-1",
                BifitEventType.ShiftClosed,
                Now.AddHours(8),
                "ReceiptSessionService",
                ShiftInternalId: "1411",
                ShiftExternalId: "352",
                ShiftOpenedAt: Now.AddHours(-6),
                ShiftClosedAt: Now.AddHours(8)),
            CancellationToken.None);

        var state = await ReadStateAsync(context);
        Assert.Null(state!.CurrentShift);

        var messages = await ReadQueuedMessagesAsync(context);
        var report = Assert.Single(messages);
        Assert.Contains("внутренняя 1411", report, StringComparison.Ordinal);
        Assert.Contains("Успешных чеков: 1", report, StringComparison.Ordinal);
        Assert.Contains("100,00", report, StringComparison.Ordinal);
        Assert.Contains("Данные неполные", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CloseOfAnotherShiftDoesNotDiscardCurrentShift()
    {
        var context = CreateContext();
        await context.Aggregator.HandleAsync(
            new BifitEvent(
                "open-1",
                BifitEventType.ShiftOpened,
                Now,
                "ReceiptSessionService",
                ShiftInternalId: "1411",
                ShiftExternalId: "352",
                ShiftOpenedAt: Now),
            CancellationToken.None);
        await context.Aggregator.HandleAsync(
            new BifitEvent("receipt-1", BifitEventType.ReceiptRegistered, Now.AddMinutes(5), "R", ReceiptId: "51665"),
            CancellationToken.None);

        // Запоздалая синхронизация уже закрытой чужой сессии.
        await context.Aggregator.HandleAsync(
            new BifitEvent(
                "close-other",
                BifitEventType.ShiftClosed,
                Now.AddMinutes(10),
                "ReceiptSessionService",
                ShiftInternalId: "999",
                ShiftExternalId: "1",
                ShiftOpenedAt: Now.AddHours(-3),
                ShiftClosedAt: Now.AddHours(-1)),
            CancellationToken.None);

        var state = await ReadStateAsync(context);
        Assert.NotNull(state!.CurrentShift);
        Assert.Equal("1411", state.CurrentShift.InternalId);
        Assert.Single(state.CurrentShift.RegisteredReceiptIds);

        var messages = await ReadQueuedMessagesAsync(context);
        Assert.Contains(
            messages,
            message => message.Contains("Смена закрыта", StringComparison.Ordinal) &&
                       message.Contains("внутренняя 999", StringComparison.Ordinal));
        Assert.DoesNotContain(
            messages,
            message => message.Contains("Смена закрыта", StringComparison.Ordinal) &&
                       message.Contains("внутренняя 1411", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShiftOpenLineAdoptsUnobservedShiftInsteadOfInterrupting()
    {
        var context = CreateContext();
        await context.Aggregator.HandleAsync(
            new BifitEvent("receipt-1", BifitEventType.ReceiptRegistered, Now, "R", ReceiptId: "51665"),
            CancellationToken.None);

        // Строка синхронизации той же смены: открытие было раньше первой наблюдённой операции.
        await context.Aggregator.HandleAsync(
            new BifitEvent(
                "open-1",
                BifitEventType.ShiftOpened,
                Now.AddMinutes(15),
                "ReceiptSessionService",
                ShiftInternalId: "1411",
                ShiftExternalId: "352",
                ShiftOpenedAt: Now.AddHours(-5)),
            CancellationToken.None);

        var state = await ReadStateAsync(context);
        Assert.NotNull(state!.CurrentShift);
        Assert.Equal("1411", state.CurrentShift.InternalId);
        Assert.Single(state.CurrentShift.RegisteredReceiptIds);
        Assert.True(state.CurrentShift.IsInferred);
        Assert.Empty(await ReadQueuedMessagesAsync(context));
    }

    private async Task<ReportState?> ReadStateAsync(TestContext context) =>
        await context.JsonFile.ReadAsync<ReportState>(context.Paths.ReportStatePath, CancellationToken.None);

    private async Task<IReadOnlyList<string>> ReadQueuedMessagesAsync(TestContext context)
    {
        var messages = new List<OutboundMessage>();
        foreach (var path in Directory.GetFiles(context.Paths.PendingQueueDirectory, "*.json"))
        {
            var message = await context.JsonFile.ReadAsync<OutboundMessage>(path, CancellationToken.None);
            if (message is not null) messages.Add(message);
        }

        return messages
            .OrderBy(message => message.CreatedAt)
            .Select(message => message.Text)
            .ToList();
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
