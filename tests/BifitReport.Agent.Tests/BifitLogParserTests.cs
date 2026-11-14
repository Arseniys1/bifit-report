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

    private BifitEvent Parse(string text, long position = 0) =>
        _parser.Parse(new LogRecord("file", position, text)) ?? throw new Xunit.Sdk.XunitException("Строка не распознана.");
}
