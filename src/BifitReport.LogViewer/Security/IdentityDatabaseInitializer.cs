using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BifitReport.LogViewer.Security;

public sealed class IdentityDatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<IdentityDatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<LogViewerDbContext>();
        await database.Database.EnsureCreatedAsync(cancellationToken);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<LogViewerUser>>();
        if (!await userManager.Users.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "No Log Viewer users exist. Run 'BifitReport.LogViewer.exe bootstrap-user --username <name>' and pass the password through stdin.");
        }
        logger.LogInformation("База пользователей Log Viewer готова.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
