using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BifitReport.Agent.Models;

namespace BifitReport.Agent.Parsing;

public sealed partial class BifitLogParser(
    ErrorClassifier errorClassifier,
    SensitiveDataRedactor redactor)
{
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd HH:mm:ss,fff",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
    ];

    public BifitEvent? Parse(LogRecord record)
    {
        var match = LogPrefixRegex().Match(record.Text);
        if (!match.Success) return null;
        if (!DateTimeOffset.TryParseExact(
                match.Groups["timestamp"].Value,
                TimestampFormats[0],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var timestamp))
        {
            return null;
        }

        var level = match.Groups["level"].Value;
        var logger = match.Groups["logger"].Value;
        var message = match.Groups["message"].Value;
        var eventIdPrefix = CreateEventId(record, timestamp);

        var shiftMatch = ShiftRegex().Match(message);
        if (shiftMatch.Success)
        {
            var internalId = NormalizeNullableValue(shiftMatch.Groups["internal"].Value);
            var externalId = NormalizeNullableValue(shiftMatch.Groups["external"].Value);
            var opened = ParseIsoTimestamp(shiftMatch.Groups["opened"].Value);
            var closedText = shiftMatch.Groups["closed"].Value;
            var closed = string.Equals(closedText, "null", StringComparison.OrdinalIgnoreCase)
                ? null
                : ParseIsoTimestamp(closedText);

            return new BifitEvent(
                eventIdPrefix + (closed is null ? ":shift-open" : ":shift-close"),
                closed is null ? BifitEventType.ShiftOpened : BifitEventType.ShiftClosed,
                timestamp,
                logger,
                ShiftInternalId: internalId,
                ShiftExternalId: externalId,
                ShiftOpenedAt: opened,
                ShiftClosedAt: closed);
        }

        var receiptMatch = ReceiptSuccessRegex().Match(message);
        if (receiptMatch.Success)
        {
            return new BifitEvent(
                eventIdPrefix + ":receipt-success",
                BifitEventType.ReceiptRegistered,
                timestamp,
                logger,
                ReceiptId: receiptMatch.Groups["id"].Value);
        }

        var deletedMatch = ReceiptDeletedRegex().Match(message);
        if (deletedMatch.Success)
        {
            return new BifitEvent(
                eventIdPrefix + ":receipt-deleted",
                BifitEventType.ReceiptDeleted,
                timestamp,
                logger,
                ReceiptId: deletedMatch.Groups["id"].Value);
        }

        var paymentValueMatch = PaymentValueRegex().Match(message);
        if (paymentValueMatch.Success && decimal.TryParse(
                paymentValueMatch.Groups["amount"].Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            return new BifitEvent(
                eventIdPrefix + ":terminal-start",
                BifitEventType.TerminalPaymentStarted,
                timestamp,
                logger,
                Amount: amount);
        }

        var paymentResultMatch = PaymentResultRegex().Match(message);
        if (paymentResultMatch.Success)
        {
            var success = string.Equals(paymentResultMatch.Groups["status"].Value, "SUCCESS", StringComparison.OrdinalIgnoreCase);
            return new BifitEvent(
                eventIdPrefix + ":terminal-result",
                BifitEventType.TerminalPaymentCompleted,
                timestamp,
                logger,
                Success: success,
                ErrorCategory: success ? null : "Платёжный терминал",
                IsCritical: !success,
                SafeMessage: success ? null : "Банковская операция завершилась неуспешно.");
        }

        if (string.Equals(level, "ERROR", StringComparison.OrdinalIgnoreCase))
        {
            var classification = errorClassifier.Classify(logger, message);
            return new BifitEvent(
                eventIdPrefix + ":error",
                BifitEventType.ApplicationError,
                timestamp,
                logger,
                ErrorCategory: classification.Category,
                IsCritical: classification.IsCritical,
                SafeMessage: redactor.Redact(message));
        }

        return null;
    }

    private static DateTimeOffset? ParseIsoTimestamp(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;

    private static string? NormalizeNullableValue(string value)
    {
        var normalized = value.Trim();
        return string.IsNullOrEmpty(normalized) || string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    private static string CreateEventId(LogRecord record, DateTimeOffset timestamp)
    {
        var source = $"{record.FileIdentity}|{record.BytePosition}|{timestamp:O}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    [GeneratedRegex("^(?<timestamp>\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2},\\d{3}) (?<level>TRACE|DEBUG|INFO|WARN|ERROR) (?<logger>\\S+) \\[[^]]+\\] (?<message>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LogPrefixRegex();

    [GeneratedRegex("Смена синхронизирована ReceiptSession\\(.*?internalSession=(?<internal>[^,]+), externalSession=(?<external>[^,]+), open=(?<opened>[^,]+), closed=(?<closed>[^,\\)]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ShiftRegex();

    [GeneratedRegex("Успешная регистрация чека\\s+(?<id>\\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ReceiptSuccessRegex();

    [GeneratedRegex("Удаляется чек с id\\s*=\\s*(?<id>\\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ReceiptDeletedRegex();

    [GeneratedRegex("Payment value:\\s*(?<amount>\\d+(?:\\.\\d{1,2})?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PaymentValueRegex();

    [GeneratedRegex("Payment result:\\s*(?<status>SUCCESS|FAIL)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PaymentResultRegex();
}
