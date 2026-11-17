using BifitReport.LogViewer.Infrastructure;

namespace BifitReport.LogViewer.Tests;

public sealed class CertificateStoreTests
{
    [Fact]
    public void NormalizesWhitespaceAndColonsInThumbprint()
    {
        var normalized = CertificateStore.NormalizeThumbprint("01:23 45:67 89:AB CD:EF 01:23 45:67 89:AB CD:EF 01:23 45:67");

        Assert.Equal("0123456789ABCDEF0123456789ABCDEF01234567", normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ZZZZ")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF012345678")]
    public void RejectsMalformedThumbprint(string value)
    {
        Assert.Throws<InvalidOperationException>(() => CertificateStore.NormalizeThumbprint(value));
    }
}
