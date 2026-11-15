using BifitReport.Agent.Configuration;
using BifitReport.Agent.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Tests;

public sealed class AgentHealthStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "bifit-health-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StatusWriteFailureDoesNotEscape()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var agentOptions = Options.Create(new AgentOptions { StateDirectory = _temporaryDirectory });
        var paths = new AppPaths(agentOptions, Options.Create(new DeliveryOptions()));

        // Занимаем путь status.json каталогом: запись файла гарантированно не удастся.
        Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "status.json"));
        var store = new AgentHealthStore(new AtomicJsonFile(), paths, NullLogger<AgentHealthStore>.Instance);

        var exception = await Record.ExceptionAsync(async () =>
        {
            await store.RecordWorkerErrorAsync("TestWorker", CancellationToken.None);
            await store.RecordLogPollAsync(42, 1, CancellationToken.None);
            await store.RecordDeliveryAsync("VK", false, "vk_1", CancellationToken.None);
        });

        // Диагностика не должна ронять агента: раньше это исключение останавливало весь хост.
        Assert.Null(exception);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
