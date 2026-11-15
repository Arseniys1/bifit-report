using BifitReport.Agent.Configuration;
using BifitReport.Agent.Infrastructure;
using BifitReport.Agent.Queue;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Tests;

public sealed class DiskMessageQueueTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "bifit-queue-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task NonRetryableMessageMovesToDeadLetterAndIsNotEnqueuedAgain()
    {
        var (queue, paths) = CreateQueue();
        Assert.True(await queue.EnqueueAsync("text", "critical:ККТ:1", CancellationToken.None));

        var message = await queue.GetNextReadyAsync(DateTimeOffset.Now.AddSeconds(1), CancellationToken.None);
        Assert.NotNull(message);
        await queue.FailAsync(message, "vk_917", CancellationToken.None);

        Assert.Null(await queue.GetNextReadyAsync(DateTimeOffset.Now.AddMinutes(5), CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(paths.FailedQueueDirectory, message.Id + ".json")));

        // Тот же отчёт не должен снова попасть в очередь после снятия с повторов.
        Assert.False(await queue.EnqueueAsync("text", "critical:ККТ:1", CancellationToken.None));
    }

    [Fact]
    public async Task MissingQueueDirectoryDoesNotThrow()
    {
        var (queue, paths) = CreateQueue();
        Assert.True(await queue.EnqueueAsync("text", "key", CancellationToken.None));
        Directory.Delete(paths.PendingQueueDirectory, recursive: true);

        var message = await queue.GetNextReadyAsync(DateTimeOffset.Now.AddSeconds(1), CancellationToken.None);

        Assert.Null(message);
    }

    private (DiskMessageQueue Queue, AppPaths Paths) CreateQueue()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var agentOptions = Options.Create(new AgentOptions { StateDirectory = _temporaryDirectory });
        var deliveryOptions = Options.Create(new DeliveryOptions());
        var jsonFile = new AtomicJsonFile();
        var paths = new AppPaths(agentOptions, deliveryOptions);
        var queue = new DiskMessageQueue(jsonFile, paths, deliveryOptions, NullLogger<DiskMessageQueue>.Instance);
        return (queue, paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, recursive: true);
    }
}
