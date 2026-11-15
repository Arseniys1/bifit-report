using System.Security.Cryptography;
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
    /// <summary>Сколько байт начала файла сравнивать, чтобы заметить подмену журнала.</summary>
    private const int MaximumHeadSignatureBytes = 1_024;

    private readonly AgentOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TailCheckpoint? _checkpoint;
    private TailCheckpoint? _committed;
    private bool _missingLogWasReported;

    public async Task<TailBatch> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var logPath = AppPaths.Normalize(_options.LogPath);
            if (_checkpoint is null)
            {
                _checkpoint = await LoadCheckpointAsync(logPath, cancellationToken);
                _committed = _checkpoint.Clone();
            }

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

            if (!string.Equals(checkpoint.LogPath, logPath, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(checkpoint.FileIdentity))
            {
                checkpoint = await CreateInitialCheckpointAsync(logPath, identity, fileInfo.Length, cancellationToken);
            }
            else if (await IsRotationAsync(checkpoint, logPath, identity, fileInfo.Length, cancellationToken))
            {
                logger.LogInformation("Обнаружена ротация журнала, чтение нового файла начинается с начала.");
                checkpoint = new TailCheckpoint
                {
                    LogPath = logPath,
                    FileIdentity = identity,
                    HeadSignature = await ComputeSignatureAsync(logPath, Math.Min(fileInfo.Length, MaximumHeadSignatureBytes), cancellationToken),
                    HeadSignatureLength = (int)Math.Min(fileInfo.Length, MaximumHeadSignatureBytes),
                    Position = 0,
                    PendingStartPosition = 0
                };
            }
            else if (checkpoint.HeadSignatureLength == 0 && fileInfo.Length > 0)
            {
                // Файл был пустым, когда создавалась контрольная точка: запоминаем
                // фактическое начало, чтобы не принять первые строки за ротацию.
                checkpoint.HeadSignatureLength = (int)Math.Min(fileInfo.Length, MaximumHeadSignatureBytes);
                checkpoint.HeadSignature = await ComputeSignatureAsync(logPath, checkpoint.HeadSignatureLength, cancellationToken);
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
            // Позиция не изменилась — не переписываем файл состояния каждые две секунды.
            if (_committed is not null && _committed.HasSameReadingState(batch.Checkpoint))
            {
                _checkpoint = batch.Checkpoint.Clone();
                return;
            }

            await jsonFile.WriteAsync(paths.TailCheckpointPath, batch.Checkpoint, cancellationToken);
            _checkpoint = batch.Checkpoint.Clone();
            _committed = batch.Checkpoint.Clone();
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
        return await CreateInitialCheckpointAsync(logPath, CreateIdentity(fileInfo), fileInfo.Length, cancellationToken);
    }

    private async Task<TailCheckpoint> CreateInitialCheckpointAsync(string logPath, string identity, long fileLength, CancellationToken cancellationToken)
    {
        var position = _options.StartFromEndOnFirstRun ? fileLength : 0;
        var signatureLength = (int)Math.Min(fileLength, MaximumHeadSignatureBytes);
        logger.LogInformation(
            "Создана начальная позиция чтения журнала {Position}. Исторические строки {HistoryMode}.",
            position,
            _options.StartFromEndOnFirstRun ? "пропускаются" : "будут обработаны");
        return new TailCheckpoint
        {
            LogPath = logPath,
            FileIdentity = identity,
            HeadSignature = await ComputeSignatureAsync(logPath, signatureLength, cancellationToken),
            HeadSignatureLength = signatureLength,
            Position = position,
            PendingStartPosition = position,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    /// <summary>
    /// Ротация определяется по трём признакам: сменилась идентичность файла, файл стал
    /// короче сохранённой позиции или изменилось начало файла. Последняя проверка нужна
    /// потому, что NTFS сохраняет время создания при пересоздании файла с тем же именем,
    /// а некоторые приложения переписывают журнал на месте.
    /// </summary>
    private static async Task<bool> IsRotationAsync(
        TailCheckpoint checkpoint,
        string logPath,
        string identity,
        long fileLength,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(checkpoint.FileIdentity, identity, StringComparison.Ordinal)) return true;
        if (fileLength < checkpoint.Position) return true;

        if (checkpoint.HeadSignatureLength <= 0 || checkpoint.HeadSignature.Length == 0) return false;
        if (fileLength < checkpoint.HeadSignatureLength) return false;

        var signature = await ComputeSignatureAsync(logPath, checkpoint.HeadSignatureLength, cancellationToken);
        return !string.Equals(checkpoint.HeadSignature, signature, StringComparison.Ordinal);
    }

    private static async Task<string> ComputeSignatureAsync(string path, long length, CancellationToken cancellationToken)
    {
        if (length <= 0) return string.Empty;

        var buffer = new byte[length];
        var read = 0;
        await using (var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4_096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            while (read < buffer.Length)
            {
                var chunk = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
                if (chunk == 0) break;
                read += chunk;
            }
        }

        return read == 0 ? string.Empty : Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
    }

    private static string CreateIdentity(FileInfo fileInfo) =>
        $"{fileInfo.CreationTimeUtc.Ticks:x16}:{fileInfo.Name}";
}
