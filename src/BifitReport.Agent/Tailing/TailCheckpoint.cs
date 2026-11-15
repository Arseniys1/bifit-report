namespace BifitReport.Agent.Tailing;

public sealed class TailCheckpoint
{
    public string LogPath { get; set; } = string.Empty;

    public string FileIdentity { get; set; } = string.Empty;

    /// <summary>
    /// Отпечаток начала файла (SHA-256 первых <see cref="HeadSignatureLength"/> байт).
    /// Время создания файла на NTFS может сохраняться при пересоздании файла с тем же
    /// именем (file system tunneling), поэтому одной идентичности недостаточно, чтобы
    /// заметить подмену или перезапись журнала.
    /// </summary>
    public string HeadSignature { get; set; } = string.Empty;

    /// <summary>Сколько первых байт файла покрывает <see cref="HeadSignature"/>.</summary>
    public int HeadSignatureLength { get; set; }

    public long Position { get; set; }

    public long PendingStartPosition { get; set; }

    public string PendingBytesBase64 { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] GetPendingBytes()
    {
        if (string.IsNullOrEmpty(PendingBytesBase64)) return [];

        try
        {
            return Convert.FromBase64String(PendingBytesBase64);
        }
        catch (FormatException)
        {
            // Повреждённое значение не должно превращаться в вечный цикл ошибок чтения:
            // незавершённая строка просто будет прочитана заново при следующем опросе.
            return [];
        }
    }

    public void SetPendingBytes(ReadOnlySpan<byte> bytes)
    {
        PendingBytesBase64 = bytes.IsEmpty ? string.Empty : Convert.ToBase64String(bytes);
    }

    public bool HasSameReadingState(TailCheckpoint other) =>
        string.Equals(LogPath, other.LogPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(FileIdentity, other.FileIdentity, StringComparison.Ordinal) &&
        string.Equals(HeadSignature, other.HeadSignature, StringComparison.Ordinal) &&
        HeadSignatureLength == other.HeadSignatureLength &&
        Position == other.Position &&
        PendingStartPosition == other.PendingStartPosition &&
        string.Equals(PendingBytesBase64, other.PendingBytesBase64, StringComparison.Ordinal);

    public TailCheckpoint Clone() => new()
    {
        LogPath = LogPath,
        FileIdentity = FileIdentity,
        HeadSignature = HeadSignature,
        HeadSignatureLength = HeadSignatureLength,
        Position = Position,
        PendingStartPosition = PendingStartPosition,
        PendingBytesBase64 = PendingBytesBase64,
        UpdatedAt = UpdatedAt
    };
}

public sealed record TailBatch(
    IReadOnlyList<Models.LogRecord> Records,
    TailCheckpoint Checkpoint,
    bool LogExists);
