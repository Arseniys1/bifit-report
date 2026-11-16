using BifitReport.LogViewer.Configuration;
using BifitReport.LogViewer.Logs;
using Microsoft.Extensions.Options;

namespace BifitReport.LogViewer.Endpoints;

public static class LogEndpoints
{
    public static RouteGroupBuilder MapLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");

        api.MapGet("/settings", (IOptions<LogViewerOptions> options, HttpContext context) => Results.Ok(new
        {
            allowRawLogs = options.Value.AllowRawLogs,
            defaultTailLines = options.Value.DefaultTailLines,
            maximumTailLines = options.Value.MaximumTailLines,
            user = context.User.Identity?.Name ?? string.Empty
        }));

        api.MapGet("/sources", (LogCatalog catalog) => Results.Ok(catalog.GetSources()));

        api.MapGet("/sources/{sourceId}/files", (
            string sourceId,
            int? page,
            int? pageSize,
            string? filter,
            LogCatalog catalog) => Results.Ok(catalog.GetFiles(sourceId, page ?? 1, pageSize ?? 100, filter)));

        api.MapGet("/sources/{sourceId}/files/{fileKey}/content", async (
            string sourceId,
            string fileKey,
            int? tail,
            string? query,
            string? level,
            bool? raw,
            LogCatalog catalog,
            LogReader reader,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var file = catalog.ResolveFile(sourceId, fileKey);
            var rawRequested = raw == true;
            loggerFactory.CreateLogger("BifitReport.LogViewer.Audit").LogInformation(
                "Пользователь {User} открыл журнал {SourceId}/{RelativePath}; raw={Raw}; remote={RemoteAddress}",
                context.User.Identity?.Name,
                sourceId,
                file.RelativePath,
                rawRequested,
                context.Connection.RemoteIpAddress);
            return Results.Ok(await reader.ReadTailAsync(file, tail ?? 0, query, level, rawRequested, cancellationToken));
        });

        api.MapGet("/sources/{sourceId}/files/{fileKey}/updates", async (
            string sourceId,
            string fileKey,
            long cursor,
            string revision,
            string? query,
            string? level,
            bool? raw,
            LogCatalog catalog,
            LogReader reader,
            CancellationToken cancellationToken) =>
        {
            var file = catalog.ResolveFile(sourceId, fileKey);
            return Results.Ok(await reader.ReadUpdatesAsync(file, cursor, revision, query, level, raw == true, cancellationToken));
        });

        return api;
    }
}
