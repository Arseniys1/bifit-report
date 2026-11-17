using BifitReport.LogViewer.Security;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace BifitReport.LogViewer.Tests;

public sealed class IdentityBootstrapCommandTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "bifit-identity-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreatesUserAndRequiresExplicitResetForExistingUser()
    {
        var configuration = CreateConfiguration();
        var firstOutput = new StringWriter();
        var firstResult = await IdentityBootstrapCommand.RunAsync(
            configuration,
            ["bootstrap-user", "--username", "admin"],
            new StringReader("Strong-Password-2026!\n"),
            firstOutput);
        var duplicateOutput = new StringWriter();
        var duplicateResult = await IdentityBootstrapCommand.RunAsync(
            configuration,
            ["bootstrap-user", "--username", "admin"],
            new StringReader("Another-Password-2026!\n"),
            duplicateOutput);
        var resetOutput = new StringWriter();
        var resetResult = await IdentityBootstrapCommand.RunAsync(
            configuration,
            ["bootstrap-user", "--username", "admin", "--reset-password"],
            new StringReader("Another-Password-2026!\n"),
            resetOutput);

        Assert.Equal(0, firstResult);
        Assert.Equal(3, duplicateResult);
        Assert.Equal(0, resetResult);
        Assert.Contains("User created", firstOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("already exists", duplicateOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("Password updated", resetOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsPasswordThatDoesNotMeetIdentityPolicy()
    {
        var output = new StringWriter();

        var result = await IdentityBootstrapCommand.RunAsync(
            CreateConfiguration(),
            ["bootstrap-user", "--username", "admin"],
            new StringReader("weak\n"),
            output);

        Assert.Equal(4, result);
        Assert.Contains("PasswordTooShort", output.ToString(), StringComparison.Ordinal);
    }

    private IConfiguration CreateConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:DatabasePath"] = Path.Combine(_temporaryDirectory, "identity.db"),
            ["Authentication:DataProtectionKeysPath"] = Path.Combine(_temporaryDirectory, "keys"),
            ["Authentication:SessionHours"] = "8"
        })
        .Build();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
