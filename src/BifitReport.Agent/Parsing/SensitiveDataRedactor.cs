using System.Text.RegularExpressions;

namespace BifitReport.Agent.Parsing;

public sealed partial class SensitiveDataRedactor
{
    public string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var result = TokenRegex().Replace(value, "$1[СКРЫТО]");
        result = EmailRegex().Replace(result, "[EMAIL]");
        result = PhoneRegex().Replace(result, "[ТЕЛЕФОН]");
        result = CardRegex().Replace(result, static match => MaskDigits(match.Value));
        result = LongIdentifierRegex().Replace(result, "[ИДЕНТИФИКАТОР]");
        return result.Length <= 300 ? result : result[..300] + "…";
    }

    private static string MaskDigits(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? $"****{digits[^4..]}" : "****";
    }

    [GeneratedRegex("(?i)(access[_-]?token|refresh[_-]?token|password|пароль|authorization)\\s*[:=]\\s*([^,;\\s}]+)")]
    private static partial Regex TokenRegex();

    [GeneratedRegex("(?i)\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b")]
    private static partial Regex EmailRegex();

    [GeneratedRegex("(?<!\\d)(?:\\+7|8)[ \\-()]*(?:\\d[ \\-()]*){10}(?!\\d)")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex("(?<!\\d)(?:\\d[ \\-]*){12,19}(?!\\d)")]
    private static partial Regex CardRegex();

    [GeneratedRegex("(?<![A-Za-z0-9+/=])[A-Za-z0-9+/]{28,}={0,2}(?![A-Za-z0-9+/=])")]
    private static partial Regex LongIdentifierRegex();
}
