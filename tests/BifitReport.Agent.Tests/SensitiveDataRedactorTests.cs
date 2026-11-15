using BifitReport.Agent.Parsing;

namespace BifitReport.Agent.Tests;

public sealed class SensitiveDataRedactorTests
{
    private readonly SensitiveDataRedactor _redactor = new();

    [Fact]
    public void RemovesSecretsAndContactData()
    {
        var source = "access_token=vk1.secret password=hunter2 mail=user@example.org phone=+7 999 123-45-67 card=2200 1234 5678 9010";
        var result = _redactor.Redact(source);

        Assert.DoesNotContain("vk1.secret", result, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result, StringComparison.Ordinal);
        Assert.DoesNotContain("user@example.org", result, StringComparison.Ordinal);
        Assert.DoesNotContain("999 123", result, StringComparison.Ordinal);
        Assert.DoesNotContain("2200 1234", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatesLongTextToRequestedLength()
    {
        var source = string.Join(" ", Enumerable.Repeat("word", 500));

        var result = _redactor.Redact(source, 50);

        Assert.True(result.Length <= 51);
        Assert.EndsWith("…", result, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsJavaClassNamesReadable()
    {
        // Имя класса ровно 28 символов: раньше правило длинных идентификаторов
        // заменяло его на [ИДЕНТИФИКАТОР] и алерт терял тип исключения.
        var source = "org.h2.jdbc.JdbcSQLNonTransientException: Внутренняя ошибка: NullPointerException";

        var result = _redactor.Redact(source, 300);

        Assert.Contains("JdbcSQLNonTransientException", result, StringComparison.Ordinal);
        Assert.DoesNotContain("[ИДЕНТИФИКАТОР]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotMaskSpaceSeparatedIdentifiers()
    {
        var source = "internalSession=1411 externalSession=352 session 1411 18266556";

        var result = _redactor.Redact(source, 300);

        Assert.Contains("1411 18266556", result, StringComparison.Ordinal);
    }

    [Fact]
    public void MasksLongBase64Token()
    {
        var source = "deviceIdentifier=O0WfamMX7WkUz/Fp+1Qs8c58vVR8YHv4Qs21AbMfb+4=";

        var result = _redactor.Redact(source, 300);

        Assert.DoesNotContain("O0WfamMX7WkUz", result, StringComparison.Ordinal);
        Assert.Contains("[ИДЕНТИФИКАТОР]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void MasksCardNumbersInDifferentFormats()
    {
        Assert.DoesNotContain("2200 1234 5678 9010", _redactor.Redact("card 2200 1234 5678 9010", 300), StringComparison.Ordinal);
        Assert.DoesNotContain("2200123456789010", _redactor.Redact("card 2200123456789010", 300), StringComparison.Ordinal);
        Assert.DoesNotContain("3782 822463 10005", _redactor.Redact("amex 3782 822463 10005", 300), StringComparison.Ordinal);
    }
}
