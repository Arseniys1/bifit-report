using BifitReport.LogViewer.Configuration;

namespace BifitReport.LogViewer.Tests;

public sealed class ConfigurationValidatorTests
{
    private readonly LogViewerOptionsValidator _validator = new();

    [Fact]
    public void RejectsRemotePlainHttp()
    {
        var result = _validator.Validate(null, new ServerOptions { ListenUrl = "http://0.0.0.0:5080" });

        Assert.True(result.Failed);
    }

    [Fact]
    public void RequiresHttpsCertificateThumbprint()
    {
        var result = _validator.Validate(null, new ServerOptions { ListenUrl = "https://0.0.0.0:7443" });

        Assert.True(result.Failed);
    }

    [Fact]
    public void RejectsRelativeIdentityStoragePaths()
    {
        var result = _validator.Validate(null, new ViewerAuthenticationOptions
        {
            DatabasePath = "identity.db",
            DataProtectionKeysPath = "keys"
        });

        Assert.True(result.Failed);
    }
}
