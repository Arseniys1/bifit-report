namespace BifitReport.LogViewer.Logs;

public sealed record LogSourceView(string Id, string DisplayName, bool Available);

public sealed record LogFileView(
    string Key,
    string Name,
    string RelativePath,
    long Size,
    DateTimeOffset LastWriteTime,
    bool IsCompressed);

public sealed record LogFilePage(
    IReadOnlyList<LogFileView> Items,
    int Page,
    int PageSize,
    int Total,
    bool CatalogTruncated);

public sealed record LogContent(
    IReadOnlyList<string> Lines,
    long? Cursor,
    string? Revision,
    bool CanFollow,
    bool ScanTruncated,
    bool Redacted);

public sealed record LogUpdate(
    IReadOnlyList<string> Lines,
    long Cursor,
    string Revision,
    bool Reset,
    bool HasMore,
    bool Redacted);

public sealed record ResolvedLogFile(
    LogSourceOptionsSnapshot Source,
    string FullPath,
    string RelativePath,
    bool IsCompressed);

public sealed record LogSourceOptionsSnapshot(
    string Id,
    string DisplayName,
    string RootPath,
    IReadOnlyList<string> Includes,
    bool Recursive);
