namespace BifitReport.Agent.Models;

public sealed record LogRecord(
    string FileIdentity,
    long BytePosition,
    string Text);
