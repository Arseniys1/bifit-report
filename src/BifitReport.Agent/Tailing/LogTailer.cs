using System.Text;
using BifitReport.Agent.Configuration;
using BifitReport.Agent.Infrastructure;
using BifitReport.Agent.Models;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Tailing;

public sealed class LogTailer(
    IOptions<AgentOptions> options,
    AtomicJsonFile jsonFile,
    AppPaths paths,
    ILogger<LogTailer> logger)
{
    private readonly AgentOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TailCheckpoint? _checkpoint;
    private bool _missingLogWasReported;

    public async Task<TailBatch> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var logPath = AppPaths.Normalize(_options.LogPath);
            _checkpoint ??= await LoadCheckpointAsync(logPath, cancellationToken);

            if (!File.Exists(logPath))
            {
                if (!_missingLogWasReported)
                {
                    logger.LogWarning("Файл журнала пока не найден: {LogPath}", logPath);
                    _missingLogWasReported = true;
                }

                return new TailBatch([], _checkpoint.Clone(), LogExists: false);
            }

            _missingLogWasReported = false;
            var fileInfo = new FileInfo(logPath);
            var identity = CreateIdentity(fileInfo);
            var checkpoint = _checkpoint.Clone();

            if (!string.Equals(checkpoint.LogPath, logPath, StringComparison.OrdinalIgnoreCase))
            {
                checkpoint = CreateInitialCheckpoint(logPath, identity, fileInfo.Length);
            }
            else if (string.IsNullOrEmpty(checkpoint.FileIdentity))
            {
                checkpoint = CreateInitialCheckpoint(logPath, identity, fileInfo.Length);
            }
            else if (!string.Equals(checkpoint.FileIdentity, identity, StringComparison.Ordinal) || fileInfo.Length < checkpoint.Position)
            {
                logger.LogInformation("Обнаружена ротация журнала, чтение нового файла начинается с начала.");
                checkpoint = new TailCheckpoint
                {
                    LogPath = logPath,
                    FileIdentity = identity,
                    Position = 0,
                    PendingStartPosition = 0
                };
            }

            if (checkpoint.Position > fileInfo.Length) checkpoint.Position = fileInfo.Length;
            var available = fileInfo.Length - checkpoint.Position;
            if (available <= 0)
            {
                checkpoint.UpdatedAt = DateTimeOffset.Now;
                return new TailBatch([], checkpoint.Clone(), LogExists: true);
            }

            var bytesToRead = (int)Math.Min(available, _options.MaxBytesPerPoll);
            var newBytes = new byte[bytesToRead];
            var bytesRead = 0;
            await using (var stream = new FileStream(
                logPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                stream.Seek(checkpoint.Position, SeekOrigin.Begin);
                while (bytesRead < newBytes.Length)
                {
                    var read = await stream.ReadAsync(newBytes.AsMemory(bytesRead), cancellationToken);
                    if (read == 0) break;
                    bytesRead += read;
                }
            }

            if (bytesRead != newBytes.Length) Array.Resize(ref newBytes, bytesRead);
            var oldPosition = checkpoint.Position;
            checkpoint.Position += bytesRead;

            var pending = checkpoint.GetPendingBytes();
            var combined = new byte[pending.Length + newBytes.Length];
            pending.CopyTo(combined, 0);
            newBytes.CopyTo(combined, pending.Length);

            var combinedStartPosition = pending.Length > 0 ? checkpoint.PendingStartPosition : oldPosition;
            var records = new List<LogRecord>();
            var lineStart = 0;

            for (var index = 0; index < combined.Length; index++)
            {
                if (combined[index] != (byte)'\n') continue;

                var length = index - lineStart;
                if (length > 0 && combined[index - 1] == (byte)'\r') length--;
                if (length <= _options.MaxLineBytes)
                {
                    var text = Encoding.UTF8.GetString(combined, lineStart, length);
                    records.Add(new LogRecord(identity, combinedStartPosition + lineStart, text));
                }
                else
                {
                    logger.LogWarning("Пропущена строка журнала размером {Length} байт: превышен безопасный лимит.", length);
                }

                lineStart = index + 1;
            }

            var remaining = combined[lineStart..];
            if (remaining.Length > _options.MaxLineBytes)
            {
                logger.LogWarning("Незавершённая строка журнала превысила безопасный лимит и была отброшена.");
                checkpoint.SetPendingBytes([]);
                checkpoint.PendingStartPosition = checkpoint.Position;
            }
            else
            {
                checkpoint.SetPendingBytes(remaining);
                checkpoint.PendingStartPosition = combinedStartPosition + lineStart;
            }

            checkpoint.UpdatedAt = DateTimeOffset.Now;
            return new TailBatch(records, checkpoint.Clone(), LogExists: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CommitAsync(TailBatch batch, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await jsonFile.WriteAsync(paths.TailCheckpointPath, batch.Checkpoint, cancellationToken);
            _checkpoint = batch.Checkpoint.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TailCheckpoint> LoadCheckpointAsync(string logPath, CancellationToken cancellationToken)
    {
        try
        {
            var saved = await jsonFile.ReadAsync<TailCheckpoint>(paths.TailCheckpointPath, cancellationToken);
            if (saved is not null) return saved;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            logger.LogWarning(exception, "Не удалось прочитать контрольную точку журнала; она будет создана заново.");
        }

        if (!File.Exists(logPath)) return new TailCheckpoint { LogPath = logPath };
        var fileInfo = new FileInfo(logPath);
        return CreateInitialCheckpoint(logPath, CreateIdentity(fileInfo), fileInfo.Length);
    }

    private TailCheckpoint CreateInitialCheckpoint(string logPath, string identity, long fileLength)
    {
        var position = _options.StartFromEndOnFirstRun ? fileLength : 0;
        logger.LogInformation(
            "Создана начальная позиция чтения журнала {Position}. Исторические строки {HistoryMode}.",
            position,
            _options.StartFromEndOnFirstRun ? "пропускаются" : "будут обработаны");
        return new TailCheckpoint
        {
            LogPath = logPath,
            FileIdentity = identity,
            Position = position,
            PendingStartPosition = position,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private static string CreateIdentity(FileInfo fileInfo) =>
        $"{fileInfo.CreationTimeUtc.Ticks:x16}:{fileInfo.Name}";
}
