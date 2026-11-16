using BifitReport.LogViewer.Configuration;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Options;

namespace BifitReport.LogViewer.Logs;

public sealed class LogCatalog(IOptions<LogViewerOptions> options, ILogger<LogCatalog> logger)
{
    private readonly LogViewerOptions _options = options.Value;

    public IReadOnlyList<LogSourceView> GetSources() => _options.Sources
        .Select(source => new LogSourceView(source.Id, source.DisplayName, Directory.Exists(NormalizeRoot(source.RootPath))))
        .ToArray();

    public LogFilePage GetFiles(string sourceId, int page, int pageSize, string? filter)
    {
        var source = GetSource(sourceId);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var normalizedFilter = filter?.Trim();
        if (normalizedFilter?.Length > 200) throw new LogRequestException("Фильтр имени файла не должен превышать 200 символов.");

        if (!Directory.Exists(source.RootPath)) return new LogFilePage([], page, pageSize, 0, false);

        var matcher = CreateMatcher(source.Includes);
        var entries = new List<LogFileView>();
        var truncated = false;
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = source.Recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            foreach (var path in Directory.EnumerateFiles(source.RootPath, "*", enumerationOptions))
            {
                var relativePath = Path.GetRelativePath(source.RootPath, path);
                if (!matcher.Match(ToGlobPath(relativePath)).HasMatches) continue;
                if (!string.IsNullOrEmpty(normalizedFilter) &&
                    !relativePath.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase)) continue;

                FileInfo file;
                try
                {
                    file = new FileInfo(path);
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    entries.Add(new LogFileView(
                        FileToken.Encode(relativePath),
                        file.Name,
                        relativePath,
                        file.Length,
                        file.LastWriteTimeUtc,
                        path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(exception, "Не удалось прочитать метаданные файла {RelativePath}", relativePath);
                }

                if (entries.Count <= _options.MaximumCatalogFiles) continue;
                entries.RemoveAt(entries.Count - 1);
                truncated = true;
                break;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Не удалось перечислить файлы источника {SourceId}", source.Id);
        }

        var ordered = entries.OrderByDescending(file => file.LastWriteTime).ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return new LogFilePage(items, page, pageSize, ordered.Length, truncated);
    }

    public ResolvedLogFile ResolveFile(string sourceId, string token)
    {
        var source = GetSource(sourceId);
        var relativePath = FileToken.Decode(token).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relativePath)) throw new LogResourceNotFoundException("Файл не найден.");
        if (!source.Recursive && relativePath.Contains(Path.DirectorySeparatorChar)) throw new LogResourceNotFoundException("Файл не найден.");

        var matcher = CreateMatcher(source.Includes);
        if (!matcher.Match(ToGlobPath(relativePath)).HasMatches) throw new LogResourceNotFoundException("Файл не найден.");

        var fullPath = Path.GetFullPath(Path.Combine(source.RootPath, relativePath));
        if (!IsWithinRoot(fullPath, source.RootPath) || !File.Exists(fullPath) || ContainsReparsePoint(fullPath, source.RootPath))
        {
            throw new LogResourceNotFoundException("Файл не найден.");
        }

        return new ResolvedLogFile(source, fullPath, relativePath, fullPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase));
    }

    private LogSourceOptionsSnapshot GetSource(string sourceId)
    {
        var configured = _options.Sources.FirstOrDefault(source => string.Equals(source.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        if (configured is null) throw new LogResourceNotFoundException("Источник журналов не найден.");
        return new LogSourceOptionsSnapshot(
            configured.Id,
            configured.DisplayName,
            NormalizeRoot(configured.RootPath),
            configured.Includes.ToArray(),
            configured.Recursive);
    }

    private static Matcher CreateMatcher(IEnumerable<string> includes)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(includes.Select(ToGlobPath));
        return matcher;
    }

    private static string NormalizeRoot(string rootPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(rootPath)));

    private static string ToGlobPath(string path) => path.Replace('\\', '/');

    private static bool IsWithinRoot(string fullPath, string rootPath)
    {
        if (string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase)) return false;
        var prefix = Path.EndsInDirectorySeparator(rootPath) ? rootPath : rootPath + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsReparsePoint(string fullPath, string rootPath)
    {
        FileSystemInfo? current = new FileInfo(fullPath);
        while (current is not null && !string.Equals(current.FullName, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }
        return false;
    }
}
