using BifitReport.Agent.Parsing;

namespace BifitReport.Agent.Tests;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void RemovesSecretsAndContactData()
    {
        var source = "access_token=vk1.secret password=hunter2 mail=user@example.org phone=+7 999 123-45-67 card=2200 1234 5678 9010";
        var result = new SensitiveDataRedactor().Redact(source);

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

        var result = new SensitiveDataRedactor().Redact(source, 50);

        Assert.True(result.Length <= 51);
        Assert.EndsWith("…", result, StringComparison.Ordinal);
    }
}
