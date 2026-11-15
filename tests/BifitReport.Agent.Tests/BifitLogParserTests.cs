using BifitReport.Agent.Models;
using BifitReport.Agent.Parsing;

namespace BifitReport.Agent.Tests;

public sealed class BifitLogParserTests
{
    private readonly BifitLogParser _parser = new(new ErrorClassifier(), new SensitiveDataRedactor());

    [Fact]
    public void ParsesShiftOpenAndClose()
    {
        var opened = Parse("2026-11-12 09:07:19,143 INFO com.bifit.cashdesk.desktop.service.ReceiptSessionService [thread] Смена синхронизирована ReceiptSession(internalId=3069, id=18266556, userOpenId=1, internalSession=1411, externalSession=352, open=2026-11-12T09:07:19.056962+05:00, closed=null, synced=false)");
        var closed = Parse("2026-11-12 20:53:29,325 INFO com.bifit.cashdesk.desktop.service.ReceiptSessionService [thread] Смена синхронизирована ReceiptSession(internalId=3069, id=18266556, userOpenId=1, internalSession=1411, externalSession=352, open=2026-11-12T09:07:19.056962+05:00, closed=2026-11-12T20:53:29.249340+05:00, synced=false)", 200);

        Assert.Equal(BifitEventType.ShiftOpened, opened.Type);
        Assert.Equal("1411", opened.ShiftInternalId);
        Assert.Equal("352", opened.ShiftExternalId);
        Assert.Null(opened.ShiftClosedAt);

        Assert.Equal(BifitEventType.ShiftClosed, closed.Type);
        Assert.NotNull(closed.ShiftClosedAt);
    }

    [Fact]
    public void ParsesReceiptAndTerminalEvents()
    {
        var receipt = Parse("2026-11-12 09:12:38,101 INFO com.bifit.cashdesk.desktop.retail.view.dialog.receipt.ReceiptPrintFragment [thread] Успешная регистрация чека 51665");
        var amount = Parse("2026-11-12 10:53:39,641 INFO com.bifit.cashdesk.desktop.retail.machina.payment_terminal.state.SelectPaymentState [thread] Payment value: 90.00", 100);
        var result = Parse("2026-11-12 10:53:52,671 INFO com.bifit.cashdesk.desktop.retail.machina.payment_terminal.state.InitialPayState [thread] Payment result: SUCCESS, id = 123", 200);

        Assert.Equal(BifitEventType.ReceiptRegistered, receipt.Type);
        Assert.Equal("51665", receipt.ReceiptId);
        Assert.Equal(90m, amount.Amount);
        Assert.True(result.Success);
    }

    [Fact]
    public void TreatsNullExternalShiftNumberAsMissing()
    {
        var shift = Parse("2026-11-15 02:21:19,143 INFO com.bifit.cashdesk.desktop.service.ReceiptSessionService [thread] Смена синхронизирована ReceiptSession(internalId=4, id=null, userOpenId=1, internalSession=4, externalSession=null, open=2026-11-12T00:50:52+05:00, closed=2026-11-15T02:21:19+05:00, synced=false)");

        Assert.Equal("4", shift.ShiftInternalId);
        Assert.Null(shift.ShiftExternalId);
    }

    [Fact]
    public void ClassifiesTerminalFailureAsCriticalWithoutTransactionDetails()
    {
        var result = Parse("2026-11-12 09:15:32,265 INFO com.bifit.cashdesk.desktop.retail.machina.payment_terminal.state.InitialPayState [thread] Payment result: FAIL, id = 631605724301");

        Assert.Equal(BifitEventType.TerminalPaymentCompleted, result.Type);
        Assert.False(result.Success);
        Assert.True(result.IsCritical);
        Assert.DoesNotContain("631605724301", result.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresStackTraceContinuation()
    {
        var record = new LogRecord("file", 0, "java.lang.RuntimeException: sensitive details");
        Assert.Null(_parser.Parse(record));
    }

    [Fact]
    public void AttachesExceptionDetailToErrorOnly()
    {
        var records = new List<LogRecord>
        {
            new("file", 0, "2026-11-15 21:15:39,562 ERROR com.bifit.cashdesk.desktop.plugin.kkm.rr_electro.RRElectro [thread] error"),
            new("file", 1, "java.lang.RuntimeException: Ошибка РР Электро -1: "),
            new("file", 2, "\tat com.bifit.cashdesk.desktop.plugin.kkm.rr_electro.RRElectro.checkResult(RRElectro.java:234) ~[rr-electro.jar:?]"),
            new("file", 3, "2026-11-15 21:15:39,564 ERROR com.bifit.cashdesk.desktop.plugin.kkm.rr_electro.RRElectro [thread] Get KKM info error"),
            new("file", 4, "java.lang.RuntimeException: Ошибка РР Электро -1: ")
        };

        var events = _parser.ParseBatch(records, 8, 900);

        Assert.Equal(2, events.Count);
        Assert.Contains("java.lang.RuntimeException", events[0].Detail, StringComparison.Ordinal);
        Assert.Contains("checkResult", events[0].Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Get KKM", events[0].Detail, StringComparison.Ordinal);
        Assert.Contains("java.lang.RuntimeException", events[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void LimitsExceptionDetailByLines()
    {
        var records = new List<LogRecord>
        {
            new("file", 0, "2026-11-15 21:15:39,562 ERROR com.bifit.cashdesk.desktop.plugin.kkm.rr_electro.RRElectro [thread] error")
        };
        for (var index = 1; index <= 20; index++)
        {
            records.Add(new LogRecord("file", index, $"\tat frame{index}"));
        }

        var events = _parser.ParseBatch(records, 3, 900);

        Assert.Single(events);
        Assert.Equal(3, events[0].Detail!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void DoesNotAttachDetailToShiftEvent()
    {
        var records = new List<LogRecord>
        {
            new("file", 0, "2026-11-15 21:15:00,000 INFO com.bifit.cashdesk.desktop.service.ReceiptSessionService [thread] Смена синхронизирована ReceiptSession(internalId=1, id=null, userOpenId=1, userCloseId=null, organizationId=1, tradeObjectId=2, deviceIdentifier=x, internalSession=1, externalSession=1, open=2026-11-15T21:15:00+05:00, closed=null, synced=false)"),
            new("file", 1, "java.lang.RuntimeException: not for shift")
        };

        var events = _parser.ParseBatch(records, 8, 900);

        Assert.Single(events);
        Assert.Null(events[0].Detail);
    }

    [Fact]
    public void RedactsSecretsInsideExceptionDetail()
    {
        var records = new List<LogRecord>
        {
            new("file", 0, "2026-11-15 21:15:39,562 ERROR com.bifit.cashdesk.desktop.plugin.kkm.rr_electro.RRElectro [thread] error"),
            new("file", 1, "java.lang.RuntimeException: access_token=vk1.secret")
        };

        var events = _parser.ParseBatch(records, 8, 900);

        Assert.Single(events);
        Assert.DoesNotContain("vk1.secret", events[0].Detail, StringComparison.Ordinal);
    }

    private BifitEvent Parse(string text, long position = 0) =>
        _parser.Parse(new LogRecord("file", position, text)) ?? throw new Xunit.Sdk.XunitException("Строка не распознана.");
}
