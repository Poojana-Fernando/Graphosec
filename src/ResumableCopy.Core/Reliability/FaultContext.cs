namespace ResumableCopy.Core.Reliability;

public sealed record FaultContext
{
    public string? SessionId { get; init; }

    public int? ChunkIndex { get; init; }

    public int AttemptNumber { get; init; }

    public Memory<byte>? Buffer { get; init; }
}
