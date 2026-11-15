using System.Text.RegularExpressions;

namespace BifitReport.Agent.Parsing;

public sealed partial class SensitiveDataRedactor
{
    public string Redact(string value) => Redact(value, 300);

    public string Redact(string value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var result = TokenRegex().Replace(value, "$1$2[СКРЫТО]");
        result = EmailRegex().Replace(result, "[EMAIL]");
        result = PhoneRegex().Replace(result, "[ТЕЛЕФОН]");
        result = CardRegex().Replace(result, static match => MaskDigits(match.Value));
        result = LongIdentifierRegex().Replace(result, "[ИДЕНТИФИКАТОР]");
        var limit = Math.Max(1, maximumLength);
        return result.Length <= limit ? result : result[..limit] + "…";
    }

    private static string MaskDigits(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? $"****{digits[^4..]}" : "****";
    }

    [GeneratedRegex("(?i)(access[_-]?token|refresh[_-]?token|password|пароль|authorization)(\\s*[:=]\\s*)([^,;\\s}]+)")]
    private static partial Regex TokenRegex();

    [GeneratedRegex("(?i)\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b")]
    private static partial Regex EmailRegex();

    [GeneratedRegex("(?<!\\d)(?:\\+7|8)[ \\-()]*(?:\\d[ \\-()]*){10}(?!\\d)")]
    private static partial Regex PhoneRegex();

    /// <summary>
    /// Номер карты: либо 13–19 цифр подряд, либо группы по четыре цифры через пробел/дефис.
    /// Без этого правила маскировались любые 12–19 цифр с разделителями, включая
    /// идентификаторы смен и чеков.
    /// </summary>
    [GeneratedRegex("(?<!\\d)(?:\\d{13,19}|\\d{4}(?:[ -]\\d{4}){2,3}|\\d{4}[ -]\\d{6}[ -]\\d{5})(?!\\d)")]
    private static partial Regex CardRegex();

    /// <summary>
    /// Длинный токен (base64/hex): маскируется только при наличии цифры или символов
    /// base64-алфавита. Иначе под правило попадали имена Java-классов вида
    /// <c>JdbcSQLNonTransientException</c> и текст ошибки становился бесполезным.
    /// </summary>
    [GeneratedRegex("(?<![A-Za-z0-9+/])(?=[A-Za-z0-9+/]*[0-9+/=])[A-Za-z0-9+/]{28,}={0,2}(?![A-Za-z0-9+/=])")]
    private static partial Regex LongIdentifierRegex();
}
