using System.Threading.RateLimiting;
using BifitReport.Core.Security;
using BifitReport.LogViewer.Configuration;
using BifitReport.LogViewer.Endpoints;
using BifitReport.LogViewer.Infrastructure;
using BifitReport.LogViewer.Logs;
using BifitReport.LogViewer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options => options.ServiceName = "BIFIT Log Viewer");
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "BIFIT_LOG_VIEWER_");

if (IdentityBootstrapCommand.IsRequested(args))
{
    Environment.ExitCode = await IdentityBootstrapCommand.RunAsync(builder.Configuration, args, Console.In, Console.Out);
    return;
}

var server = builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions();
builder.WebHost.UseUrls(server.ListenUrl);
if (Uri.TryCreate(server.ListenUrl, UriKind.Absolute, out var listenUri) && listenUri.Scheme == Uri.UriSchemeHttps)
{
    var certificate = CertificateStore.LoadServerCertificate(server.CertificateThumbprint);
    builder.WebHost.ConfigureKestrel(options =>
        options.ConfigureHttpsDefaults(https => https.ServerCertificate = certificate));
}

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "BifitReport.LogViewer"));

builder.Services.AddOptions<LogViewerOptions>()
    .Bind(builder.Configuration.GetSection(LogViewerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<ViewerAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(ViewerAuthenticationOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<ServerOptions>()
    .Bind(builder.Configuration.GetSection(ServerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<LogViewerOptions>, LogViewerOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<ViewerAuthenticationOptions>, LogViewerOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<ServerOptions>, LogViewerOptionsValidator>();

var authenticationOptions = builder.Configuration
    .GetSection(ViewerAuthenticationOptions.SectionName)
    .Get<ViewerAuthenticationOptions>() ?? new ViewerAuthenticationOptions();
builder.Services.AddLogViewerIdentity(authenticationOptions);
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "BifitReport.LogViewer.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var key = context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 240,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<LogServiceExceptionHandler>();
builder.Services.AddHealthChecks()
    .AddCheck<LogSourcesHealthCheck>("log-sources", tags: ["ready"]);
builder.Services.AddSingleton<SensitiveDataRedactor>();
builder.Services.AddSingleton<LogCatalog>();
builder.Services.AddSingleton<LogReader>();
builder.Services.AddHostedService<IdentityDatabaseInitializer>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnostics, context) =>
    {
        diagnostics.Set("RemoteAddress", context.Connection.RemoteIpAddress);
        diagnostics.Set("User", context.User.Identity?.Name);
    };
});
if (listenUri?.Scheme == Uri.UriSchemeHttps) app.UseHsts();

app.Use(async (context, next) =>
{
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

app.UseWhen(
    context => context.Request.Path.Equals("/app.css", StringComparison.OrdinalIgnoreCase),
    branch => branch.UseStaticFiles());
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseStaticFiles();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("ready") }).AllowAnonymous();
app.MapRazorPages();
app.MapPost("/auth/logout", async (
    HttpContext context,
    IAntiforgery antiforgery,
    SignInManager<LogViewerUser> signInManager) =>
{
    await antiforgery.ValidateRequestAsync(context);
    await signInManager.SignOutAsync();
    return Results.NoContent();
});
app.MapLogEndpoints();
app.MapGet("/", (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.WebRootPath, "index.html"), "text/html; charset=utf-8"));

await app.RunAsync();
