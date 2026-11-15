using BifitReport.Agent.Configuration;
using BifitReport.Agent.Models;
using BifitReport.Agent.Reporting;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Tests;

public sealed class ReportFormatterTests
{
    [Fact]
    public void CriticalAlertContainsSanitizedDetail()
    {
        var formatter = new ReportFormatter(Options.Create(new ReportOptions
        {
            IncludeSanitizedErrorMessage = true,
            IncludeExceptionDetails = true
        }));
        var detail = string.Join(Environment.NewLine, Enumerable.Range(1, 8).Select(index => $"at frame{index}"));

        var text = formatter.FormatCriticalError(CreateError() with { Detail = detail });

        Assert.Contains("Ошибка: Ошибка инициализации плагина ТСПИоТ", text, StringComparison.Ordinal);
        Assert.Contains("Детали:", text, StringComparison.Ordinal);
        Assert.Contains("frame8", text, StringComparison.Ordinal);
        Assert.True(text.Length < 4000);
    }

    [Fact]
    public void GenericErrorWordIsNotDuplicated()
    {
        var formatter = new ReportFormatter(Options.Create(new ReportOptions
        {
            IncludeSanitizedErrorMessage = true,
            IncludeExceptionDetails = true
        }));

        var text = formatter.FormatCriticalError(CreateError() with { SafeMessage = "error", Detail = "java.lang.RuntimeException: real reason" });

        Assert.DoesNotContain("Ошибка: error", text, StringComparison.Ordinal);
        Assert.Contains("real reason", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CriticalAlertWithoutDetailKeepsPrivacyFooter()
    {
        var formatter = new ReportFormatter(Options.Create(new ReportOptions()));

        var text = formatter.FormatCriticalError(CreateError());

        Assert.DoesNotContain("Детали:", text, StringComparison.Ordinal);
        Assert.Contains("Полная строка журнала", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailIsNotIncludedWhenDisabled()
    {
        var formatter = new ReportFormatter(Options.Create(new ReportOptions { IncludeExceptionDetails = false }));

        var text = formatter.FormatCriticalError(CreateError() with { Detail = "stack frame" });

        Assert.DoesNotContain("stack frame", text, StringComparison.Ordinal);
    }

    private static BifitEvent CreateError() => new(
        "id",
        BifitEventType.ApplicationError,
        DateTimeOffset.Now,
        "com.bifit.cashdesk.desktop.plugin.kkm.rr_electro.RRElectro",
        ErrorCategory: "ККТ/фискализация",
        IsCritical: true,
        SafeMessage: "Ошибка инициализации плагина ТСПИоТ");
}
