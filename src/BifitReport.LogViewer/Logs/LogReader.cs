using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BifitReport.Core.Security;
using BifitReport.LogViewer.Configuration;
using Microsoft.Extensions.Options;

namespace BifitReport.LogViewer.Logs;

public sealed class LogReader(
    IOptions<LogViewerOptions> options,
    SensitiveDataRedactor redactor,
    ILogger<LogReader> logger)
{
    private static readonly UTF8Encoding LenientUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
    private readonly LogViewerOptions _options = options.Value;

    public async Task<LogContent> ReadTailAsync(
        ResolvedLogFile file,
        int requestedLines,
        string? query,
        string? level,
        bool raw,
        CancellationToken cancellationToken)
    {
        EnsureRawAccess(raw);
        var lineCount = requestedLines <= 0 ? _options.DefaultTailLines : Math.Min(requestedLines, _options.MaximumTailLines);
        var normalizedQuery = NormalizeQuery(query);
        var normalizedLevel = NormalizeLevel(level);

        return file.IsCompressed
            ? await ReadCompressedAsync(file, lineCount, normalizedQuery, normalizedLevel, raw, cancellationToken)
            : await ReadRegularAsync(file, lineCount, normalizedQuery, normalizedLevel, raw, cancellationToken);
    }

    public async Task<LogUpdate> ReadUpdatesAsync(
        ResolvedLogFile file,
        long cursor,
        string revision,
        string? query,
        string? level,
        bool raw,
        CancellationToken cancellationToken)
    {
        EnsureRawAccess(raw);
        var normalizedQuery = NormalizeQuery(query);
        var normalizedLevel = NormalizeLevel(level);
        if (file.IsCompressed) throw new LogRequestException("Для сжатых журналов live-tail недоступен.");
        if (cursor < 0) throw new LogRequestException("Некорректная позиция чтения.");
        if (string.IsNullOrWhiteSpace(revision) || revision.Length > 256) throw new LogRequestException("Некорректная ревизия файла.");

        await using var stream = OpenShared(file.FullPath);
        var currentRevision = await CreateRevisionAsync(stream, cancellationToken);
        if (!string.Equals(revision, currentRevision, StringComparison.Ordinal) || cursor > stream.Length)
        {
            logger.LogInformation("Live-tail обнаружил замену файла {SourceId}/{RelativePath}", file.Source.Id, file.RelativePath);
            return new LogUpdate([], 0, currentRevision, Reset: true, HasMore: false, Redacted: !raw);
        }

        var available = stream.Length - cursor;
        if (available <= 0) return new LogUpdate([], cursor, currentRevision, Reset: false, HasMore: false, Redacted: !raw);

        var requestedBytes = (int)Math.Min(available, _options.MaximumLiveReadBytes);
        var buffer = new byte[requestedBytes];
        stream.Position = cursor;
        var bytesRead = await ReadAtMostAsync(stream, buffer, cancellationToken);
        if (bytesRead == 0) return new LogUpdate([], cursor, currentRevision, Reset: false, HasMore: false, Redacted: !raw);

        var completeLength = LastCompleteLineLength(buffer.AsSpan(0, bytesRead));
        if (completeLength == 0)
        {
            if (bytesRead < _options.MaximumLiveReadBytes)
            {
                return new LogUpdate([], cursor, currentRevision, Reset: false, HasMore: false, Redacted: !raw);
            }

            completeLength = bytesRead;
        }

        var text = LenientUtf8.GetString(buffer, 0, completeLength);
        var lines = SplitLines(text)
            .Where(line => Matches(line, normalizedQuery, normalizedLevel))
            .Select(line => PrepareLine(line, raw))
            .ToArray();
        var nextCursor = cursor + completeLength;
        return new LogUpdate(lines, nextCursor, currentRevision, Reset: false, HasMore: nextCursor < stream.Length, Redacted: !raw);
    }

    private async Task<LogContent> ReadRegularAsync(
        ResolvedLogFile file,
        int lineCount,
        string? query,
        string? level,
        bool raw,
        CancellationToken cancellationToken)
    {
        await using var stream = OpenShared(file.FullPath);
        var revision = await CreateRevisionAsync(stream, cancellationToken);
        var length = stream.Length;
        var bytesToRead = (int)Math.Min(length, _options.MaximumScanBytes);
        var start = length - bytesToRead;
        stream.Position = start;
        var buffer = new byte[bytesToRead];
        var bytesRead = await ReadAtMostAsync(stream, buffer, cancellationToken);

        var contentStart = 0;
        if (start > 0)
        {
            var firstNewline = buffer.AsSpan(0, bytesRead).IndexOf((byte)'\n');
            contentStart = firstNewline >= 0 ? firstNewline + 1 : bytesRead;
        }

        var completeLength = LastCompleteLineLength(buffer.AsSpan(contentStart, bytesRead - contentStart));
        var text = completeLength == 0
            ? string.Empty
            : LenientUtf8.GetString(buffer, contentStart, completeLength);
        var lines = SplitLines(text)
            .Where(line => Matches(line, query, level))
            .TakeLast(lineCount)
            .Select(line => PrepareLine(line, raw))
            .ToArray();
        var cursor = start + contentStart + completeLength;

        return new LogContent(lines, cursor, revision, CanFollow: true, ScanTruncated: start > 0, Redacted: !raw);
    }

    private async Task<LogContent> ReadCompressedAsync(
        ResolvedLogFile file,
        int lineCount,
        string? query,
        string? level,
        bool raw,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(file.FullPath);
        if (info.Length > _options.MaximumCompressedFileBytes)
        {
            throw new LogLimitExceededException("Сжатый журнал превышает настроенный лимит размера.");
        }

        var result = new Queue<string>(lineCount);
        long decompressedCharacters = 0;
        await using var fileStream = OpenShared(file.FullPath);
        await using var gzip = new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new StreamReader(gzip, LenientUtf8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: false);

        var buffer = new char[64 * 1024];
        var currentLine = new StringBuilder(Math.Min(_options.MaximumLineCharacters, 4_096));
        var lineWasTruncated = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            decompressedCharacters += read;
            if (decompressedCharacters > _options.MaximumDecompressedFileBytes)
            {
                throw new LogLimitExceededException("Распакованный журнал превышает настроенный лимит размера.");
            }

            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                if (character == '\n')
                {
                    AddLine(currentLine.ToString().TrimEnd('\r'), lineWasTruncated);
                    currentLine.Clear();
                    lineWasTruncated = false;
                }
                else if (currentLine.Length < _options.MaximumLineCharacters)
                {
                    currentLine.Append(character);
                }
                else
                {
                    lineWasTruncated = true;
                }
            }
        }

        if (currentLine.Length > 0 || lineWasTruncated) AddLine(currentLine.ToString().TrimEnd('\r'), lineWasTruncated);

        return new LogContent(result.ToArray(), null, null, CanFollow: false, ScanTruncated: false, Redacted: !raw);

        void AddLine(string line, bool truncated)
        {
            if (truncated) line += "…";
            if (!Matches(line, query, level)) return;
            if (result.Count == lineCount) result.Dequeue();
            result.Enqueue(PrepareLine(line, raw));
        }
    }

    private string PrepareLine(string line, bool raw)
    {
        if (line.Length > _options.MaximumLineCharacters) line = line[.._options.MaximumLineCharacters] + "…";
        return raw ? line : redactor.Redact(line, _options.MaximumLineCharacters);
    }

    private void EnsureRawAccess(bool raw)
    {
        if (raw && !_options.AllowRawLogs) throw new LogResourceNotFoundException("Raw-доступ к журналам отключён.");
    }

    private static string? NormalizeQuery(string? query)
    {
        var value = query?.Trim();
        if (value?.Length > 200) throw new LogRequestException("Поисковая строка не должна превышать 200 символов.");
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? NormalizeLevel(string? level)
    {
        var value = level?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(value) || value == "ALL") return null;
        return value is "TRACE" or "DEBUG" or "INFO" or "WARN" or "ERROR"
            ? value
            : throw new LogRequestException("Неизвестный уровень журнала.");
    }

    private static bool Matches(string line, string? query, string? level)
    {
        if (query is not null && !line.Contains(query, StringComparison.OrdinalIgnoreCase)) return false;
        return level is null || line.Contains($" {level} ", StringComparison.Ordinal);
    }

    private static FileStream OpenShared(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 64 * 1024,
        options: FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task<int> ReadAtMostAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static int LastCompleteLineLength(ReadOnlySpan<byte> bytes)
    {
        var lastNewline = bytes.LastIndexOf((byte)'\n');
        return lastNewline < 0 ? 0 : lastNewline + 1;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        var lines = text.Split('\n');
        var count = lines.Length;
        if (count > 0 && lines[^1].Length == 0) count--;
        for (var index = 0; index < count; index++)
        {
            var line = lines[index].TrimEnd('\r');
            yield return index == 0 ? line.TrimStart('\uFEFF') : line;
        }
    }

    private static async Task<string> CreateRevisionAsync(FileStream stream, CancellationToken cancellationToken)
    {
        var originalPosition = stream.Position;
        stream.Position = 0;
        var head = new byte[(int)Math.Min(stream.Length, 1_024)];
        var read = await ReadAtMostAsync(stream, head, cancellationToken);
        stream.Position = originalPosition;
        var firstNewline = read == 0 ? -1 : head.AsSpan(0, read).IndexOf((byte)'\n');
        var signatureLength = firstNewline >= 0 ? firstNewline + 1 : read;
        var hash = signatureLength == 0
            ? "empty"
            : Convert.ToHexString(SHA256.HashData(head.AsSpan(0, signatureLength)))[..16];
        return $"{File.GetCreationTimeUtc(stream.Name).Ticks:x16}-{hash}";
    }
}
