using BifitReport.LogViewer.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BifitReport.LogViewer.Security;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddLogViewerIdentity(
        this IServiceCollection services,
        ViewerAuthenticationOptions options)
    {
        var databasePath = Normalize(options.DatabasePath);
        var keysPath = Normalize(options.DataProtectionKeysPath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        Directory.CreateDirectory(keysPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Cache = SqliteCacheMode.Shared,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        services.AddDbContext<LogViewerDbContext>(dbOptions => dbOptions.UseSqlite(connectionString));
        services.AddIdentityCore<LogViewerUser>(identityOptions =>
            {
                identityOptions.Password.RequiredLength = 12;
                identityOptions.Password.RequireDigit = true;
                identityOptions.Password.RequireLowercase = true;
                identityOptions.Password.RequireUppercase = true;
                identityOptions.Password.RequireNonAlphanumeric = true;
                identityOptions.Lockout.AllowedForNewUsers = true;
                identityOptions.Lockout.MaxFailedAccessAttempts = 5;
                identityOptions.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                identityOptions.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<LogViewerDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();
        services.ConfigureApplicationCookie(cookieOptions =>
        {
            cookieOptions.Cookie.Name = "BifitReport.LogViewer.Auth";
            cookieOptions.Cookie.HttpOnly = true;
            cookieOptions.Cookie.SameSite = SameSiteMode.Strict;
            cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            cookieOptions.LoginPath = "/login";
            cookieOptions.AccessDeniedPath = "/login";
            cookieOptions.ExpireTimeSpan = TimeSpan.FromHours(options.SessionHours);
            cookieOptions.SlidingExpiration = true;
            cookieOptions.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context => HandleApiRedirect(context, StatusCodes.Status401Unauthorized),
                OnRedirectToAccessDenied = context => HandleApiRedirect(context, StatusCodes.Status403Forbidden)
            };
        });

        services.AddDataProtection()
            .SetApplicationName("BifitReport.LogViewer")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .ProtectKeysWithDpapi(protectToLocalMachine: true);

        return services;
    }

    private static Task HandleApiRedirect(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = statusCode;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }
        return Task.CompletedTask;
    }

    private static string Normalize(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (!Path.IsPathFullyQualified(expanded)) throw new InvalidOperationException("Identity storage paths must be absolute.");
        return Path.GetFullPath(expanded);
    }
}
