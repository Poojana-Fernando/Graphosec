namespace ResumableCopy.Core.Reliability;

public sealed class FaultRule
{
    public required FaultPoint Point { get; init; }

    public FaultKind Kind { get; init; } = FaultKind.ReadFailure;

    public int? ChunkIndex { get; init; }

    public int Occurrence { get; init; } = 1;

    public int CorruptByteOffset { get; init; }

    public byte CorruptByteValue { get; init; } = 0xFF;

    public int DelayMilliseconds { get; init; }
}
