using System.Globalization;
using System.Text;
using BifitReport.Agent.Configuration;
using BifitReport.Agent.Models;
using BifitReport.Agent.State;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Reporting;

public sealed class ReportFormatter(IOptions<ReportOptions> options)
{
    private readonly ReportOptions _options = options.Value;
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    public string FormatShiftOpened(ShiftReportState shift)
    {
        var builder = Header("Смена открыта");
        builder.AppendLine($"Смена: {DisplayShift(shift)}");
        builder.AppendLine($"Время: {FormatTime(shift.OpenedAt)}");
        builder.Append("Агент отчётов подключён и наблюдает за журналом кассы.");
        return builder.ToString();
    }

    public string FormatShiftClosed(ShiftReportState shift)
    {
        var builder = Header("Смена закрыта");
        builder.AppendLine($"Смена: {DisplayShift(shift)}");
        builder.AppendLine($"Период: {FormatTime(shift.OpenedAt)} — {FormatTime(shift.ClosedAt ?? DateTimeOffset.Now)}");
        if (shift.IsInferred)
        {
            builder.AppendLine("Данные неполные: агент не наблюдал открытие этой смены.");
        }

        AppendCounters(builder, shift);
        builder.AppendLine();
        builder.Append("Примечание: сумма отражает только распознанные успешные операции терминала и не является полной бухгалтерской выручкой.");
        return builder.ToString();
    }

    public string FormatPeriodicSummary(ShiftReportState shift, DateTimeOffset now)
    {
        var builder = Header("Промежуточный отчёт");
        builder.AppendLine($"Смена: {DisplayShift(shift)}");
        builder.AppendLine($"Период: {FormatTime(shift.OpenedAt)} — {FormatTime(now)}");
        AppendCounters(builder, shift);
        return builder.ToString();
    }

    public string FormatCriticalError(BifitEvent bifitEvent)
    {
        var builder = Header("Требуется внимание");
        builder.AppendLine($"Категория: {bifitEvent.ErrorCategory ?? "Ошибка кассы"}");
        builder.AppendLine($"Время: {FormatTime(bifitEvent.Timestamp)}");
        builder.AppendLine($"Источник: {ShortLoggerName(bifitEvent.Logger)}");
        if (_options.IncludeSanitizedErrorMessage && !string.IsNullOrWhiteSpace(bifitEvent.SafeMessage))
        {
            builder.AppendLine($"Сообщение: {bifitEvent.SafeMessage}");
        }

        builder.Append("Проверьте кассу на рабочем месте. Полная строка журнала в VK не отправляется.");
        return builder.ToString();
    }

    public string FormatInterruptedShift(ShiftReportState shift, DateTimeOffset detectedAt)
    {
        var builder = Header("Обнаружена новая смена");
        builder.AppendLine($"Предыдущая смена {DisplayShift(shift)} не имела события закрытия в журнале.");
        builder.AppendLine($"Новая смена обнаружена: {FormatTime(detectedAt)}");
        AppendCounters(builder, shift);
        return builder.ToString();
    }

    private StringBuilder Header(string title)
    {
        var builder = new StringBuilder();
        builder.Append("БИФИТ Касса — ").AppendLine(title);
        if (!string.IsNullOrWhiteSpace(_options.StoreName)) builder.Append("Точка: ").AppendLine(_options.StoreName.Trim());
        return builder;
    }

    private static void AppendCounters(StringBuilder builder, ShiftReportState shift)
    {
        builder.AppendLine($"Успешных чеков: {shift.RegisteredReceiptIds.Count}");
        builder.AppendLine($"Удалённых незавершённых чеков: {shift.DeletedReceiptIds.Count}");
        builder.AppendLine($"Терминал: {shift.TerminalSuccessCount} успешно, {shift.TerminalFailureCount} неуспешно");
        builder.AppendLine($"Распознанная сумма терминала: {shift.RecognizedTerminalSuccessAmount.ToString("N2", RussianCulture)} ₽");
        builder.AppendLine($"Ошибок в журнале: {shift.ErrorCount}, критических: {shift.CriticalErrorCount}");
    }

    private static string DisplayShift(ShiftReportState shift) =>
        shift.ExternalId is not "не определена"
            ? $"№ {shift.ExternalId} (внутренняя {shift.InternalId})"
            : $"внутренняя {shift.InternalId}";

    private static string FormatTime(DateTimeOffset value) => value.ToString("dd.MM.yyyy HH:mm:ss zzz", RussianCulture);

    private static string ShortLoggerName(string logger)
    {
        var index = logger.LastIndexOf('.');
        return index >= 0 && index < logger.Length - 1 ? logger[(index + 1)..] : logger;
    }
}
