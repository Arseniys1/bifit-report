using BifitReport.Agent.Configuration;
using BifitReport.Agent.Infrastructure;
using BifitReport.Agent.State;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Archiving;

/// <summary>
/// Копирует ротированные журналы кассы в отдельный архив, чтобы они пережили
/// трёхмесячную очистку, которую выполняет БИФИТ. Источник только читается,
/// ничего не удаляется и не изменяется.
/// </summary>
public sealed class LogArchiver(
    IOptions<ArchiveOptions> options,
    AtomicJsonFile jsonFile,
    AppPaths paths,
    ILogger<LogArchiver> logger)
{
    private readonly ArchiveOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ArchiveStatus> RunAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var status = new ArchiveStatus
            {
                UpdatedAt = DateTimeOffset.Now,
                LastRunAt = DateTimeOffset.Now,
                SourceCount = _options.Sources.Count
            };

            var destinationRoot = Normalize(_options.Destination);
            var minimumAge = TimeSpan.FromMinutes(_options.MinFileAgeMinutes);

            foreach (var source in _options.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceRoot = Normalize(source.Path);
                if (!Directory.Exists(sourceRoot))
                {
                    status.Failed++;
                    status.LastError = $"Каталог журналов не найден: {sourceRoot}";
                    logger.LogWarning("Каталог журналов кассы {SourceName} не найден: {SourcePath}", source.Name, sourceRoot);
                    continue;
                }

                try
                {
                    await ArchiveSourceAsync(source, sourceRoot, destinationRoot, minimumAge, status, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    status.Failed++;
                    status.LastError = $"{source.Name}: {exception.Message}";
                    logger.LogWarning(exception, "Не удалось обновить архив журналов кассы {SourceName}.", source.Name);
                }
            }

            status.UpdatedAt = DateTimeOffset.Now;
            await jsonFile.WriteAsync(paths.ArchiveStatusPath, status, cancellationToken);
            logger.LogInformation(
                "Архив журналов: скопировано {Copied}, пропущено {Skipped}, ошибок {Failed}.",
                status.Copied,
                status.Skipped,
                status.Failed);
            return status;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ArchiveSourceAsync(
        ArchiveSource source,
        string sourceRoot,
        string destinationRoot,
        TimeSpan minimumAge,
        ArchiveStatus status,
        CancellationToken cancellationToken)
    {
        var targetDirectory = Path.Combine(destinationRoot, source.Name);
        Directory.CreateDirectory(targetDirectory);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        foreach (var pattern in _options.Patterns)
        {
            foreach (var file in Directory.EnumerateFiles(sourceRoot, pattern, SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(file)) continue;

                try
                {
                    var sourceInfo = new FileInfo(file);
                    if (!sourceInfo.Exists || now - sourceInfo.LastWriteTimeUtc < minimumAge)
                    {
                        status.Skipped++;
                        continue;
                    }

                    var targetPath = Path.Combine(targetDirectory, sourceInfo.Name);
                    var targetInfo = new FileInfo(targetPath);
                    if (targetInfo.Exists &&
                        targetInfo.Length == sourceInfo.Length &&
                        targetInfo.LastWriteTimeUtc == sourceInfo.LastWriteTimeUtc)
                    {
                        status.Skipped++;
                        continue;
                    }

                    await CopyAtomicAsync(sourceInfo, targetPath, cancellationToken);
                    status.Copied++;
                    logger.LogInformation("Журнал сохранён в архив: {SourceFile} -> {TargetFile}", file, targetPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    status.Failed++;
                    status.LastError = $"{Path.GetFileName(file)}: {exception.Message}";
                    logger.LogWarning(exception, "Не удалось скопировать журнал в архив: {SourceFile}", file);
                }
            }
        }
    }

    private static async Task CopyAtomicAsync(FileInfo sourceInfo, string targetPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var temporaryPath = $"{targetPath}.{Guid.NewGuid():N}.part";
        try
        {
            await using (var source = new FileStream(
                sourceInfo.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(target, cancellationToken);
                await target.FlushAsync(cancellationToken);
                if (target.Length != source.Length)
                {
                    throw new IOException($"Размер копии не совпал: {target.Length} вместо {source.Length} байт.");
                }
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
            File.SetLastWriteTimeUtc(targetPath, sourceInfo.LastWriteTimeUtc);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
}
