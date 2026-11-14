namespace BifitReport.Agent.Tailing;

public sealed class TailCheckpoint
{
    public string LogPath { get; set; } = string.Empty;
    public string FileIdentity { get; set; } = string.Empty;
    public long Position { get; set; }
    public long PendingStartPosition { get; set; }
    public string PendingBytesBase64 { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] GetPendingBytes() => string.IsNullOrEmpty(PendingBytesBase64)
        ? []
        : Convert.FromBase64String(PendingBytesBase64);

    public void SetPendingBytes(ReadOnlySpan<byte> bytes)
    {
        PendingBytesBase64 = bytes.IsEmpty ? string.Empty : Convert.ToBase64String(bytes);
    }

    public TailCheckpoint Clone() => new()
    {
        LogPath = LogPath,
        FileIdentity = FileIdentity,
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
