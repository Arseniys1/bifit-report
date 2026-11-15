using BifitReport.Agent.Configuration;
using Microsoft.Extensions.Configuration;

namespace BifitReport.Agent.Tests;

public sealed class ConfigurationValidatorTests
{
    private static ConfigurationValidator CreateValidator(string deliveryMode) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Delivery:Mode"] = deliveryMode })
            .Build());

    [Fact]
    public void VkModeWithoutRecipientsIsRejected()
    {
        var result = CreateValidator("VK").Validate(null, new VkOptions { PeerIds = [] });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Vk:PeerIds", StringComparison.Ordinal));
    }

    [Fact]
    public void VkModeWithRecipientIsAccepted()
    {
        var result = CreateValidator("VK").Validate(null, new VkOptions { PeerIds = [2000000001] });

        Assert.False(result.Failed);
    }

    [Fact]
    public void FileModeWithoutRecipientsIsAccepted()
    {
        var result = CreateValidator("File").Validate(null, new VkOptions { PeerIds = [] });

        Assert.False(result.Failed);
    }

    [Fact]
    public void NonPositivePeerIdsAreRejected()
    {
        var result = CreateValidator("VK").Validate(null, new VkOptions { PeerIds = [0, -5] });

        Assert.True(result.Failed);
    }

    [Fact]
    public void ApiBaseUrlAlwaysGetsTrailingSlashForMethodResolution()
    {
        Assert.Equal("https://api.vk.ru/method/", new VkOptions { ApiBaseUrl = "https://api.vk.ru/method" }.NormalizedApiBaseUrl);
        Assert.Equal("https://api.vk.com/method/", new VkOptions { ApiBaseUrl = "https://api.vk.com/method/" }.NormalizedApiBaseUrl);
    }

    [Fact]
    public void DisabledArchiveDoesNotRequireConfiguration()
    {
        var result = CreateArchiveValidator().Validate(null, new ArchiveOptions());

        Assert.False(result.Failed);
    }

    [Fact]
    public void ValidArchiveConfigurationIsAccepted()
    {
        var options = new ArchiveOptions
        {
            Enabled = true,
            Destination = @"E:\bifit\archive\bifit-logs",
            Sources = [new ArchiveSource { Name = "kassa-1", Path = @"C:\ProgramData\BIFIT\retail\logs" }]
        };

        var result = CreateArchiveValidator().Validate(null, options);

        Assert.False(result.Failed);
    }

    [Fact]
    public void ArchiveDestinationInsideSourceIsRejected()
    {
        var source = Path.Combine(Path.GetTempPath(), "bifit-archive-source");
        var options = new ArchiveOptions
        {
            Enabled = true,
            Destination = Path.Combine(source, "archive"),
            Sources = [new ArchiveSource { Name = "kassa", Path = source }]
        };

        var result = CreateArchiveValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("внутри источника", StringComparison.Ordinal));
    }

    [Fact]
    public void ArchiveWithDuplicateSourceNamesIsRejected()
    {
        var options = new ArchiveOptions
        {
            Enabled = true,
            Destination = Path.Combine(Path.GetTempPath(), "bifit-archive-destination"),
            Sources =
            [
                new ArchiveSource { Name = "kassa", Path = @"C:\one" },
                new ArchiveSource { Name = "kassa", Path = @"C:\two" }
            ]
        };

        var result = CreateArchiveValidator().Validate(null, options);

        Assert.True(result.Failed);
    }

    private static ConfigurationValidator CreateArchiveValidator() =>
        new(new ConfigurationBuilder().Build());
}
