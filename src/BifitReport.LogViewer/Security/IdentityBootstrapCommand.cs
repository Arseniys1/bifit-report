using BifitReport.LogViewer.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BifitReport.LogViewer.Security;

public static class IdentityBootstrapCommand
{
    private const string CommandName = "bootstrap-user";

    public static bool IsRequested(string[] args) =>
        args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(IConfiguration configuration, string[] args, TextReader input, TextWriter output)
    {
        var username = GetArgument(args, "--username");
        var resetPassword = args.Any(argument => string.Equals(argument, "--reset-password", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(username) || username.Length > 100)
        {
            await output.WriteLineAsync("A username between 1 and 100 characters is required: --username <name>.");
            return 2;
        }

        var password = await input.ReadLineAsync();
        if (string.IsNullOrEmpty(password))
        {
            await output.WriteLineAsync("A password must be provided through standard input.");
            return 2;
        }

        var authenticationOptions = configuration
            .GetSection(ViewerAuthenticationOptions.SectionName)
            .Get<ViewerAuthenticationOptions>() ?? new ViewerAuthenticationOptions();
        var validator = new LogViewerOptionsValidator();
        var validation = validator.Validate(Options.DefaultName, authenticationOptions);
        if (validation.Failed)
        {
            await output.WriteLineAsync(string.Join(Environment.NewLine, validation.Failures));
            return 2;
        }

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddLogViewerIdentity(authenticationOptions);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<LogViewerDbContext>();
        await database.Database.EnsureCreatedAsync();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<LogViewerUser>>();
        var user = await userManager.FindByNameAsync(username);

        IdentityResult result;
        if (user is null)
        {
            user = new LogViewerUser { UserName = username };
            result = await userManager.CreateAsync(user, password);
        }
        else if (resetPassword)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            result = await userManager.ResetPasswordAsync(user, token, password);
        }
        else
        {
            await output.WriteLineAsync("The user already exists. Add --reset-password to replace its password.");
            return 3;
        }

        password = string.Empty;
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) await output.WriteLineAsync($"{error.Code}: {error.Description}");
            return 4;
        }

        await output.WriteLineAsync(resetPassword ? "Password updated." : "User created.");
        return 0;
    }

    private static string? GetArgument(string[] args, string name)
    {
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        }
        return null;
    }
}
