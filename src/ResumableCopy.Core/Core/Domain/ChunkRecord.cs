namespace ResumableCopy.Core.Domain;

public sealed class ChunkRecord
{
    public required int Index { get; init; }

    public required long Offset { get; init; }

    public required int Length { get; init; }

    public byte[]? Hash { get; set; }

    public bool IsComplete { get; set; }

    public ChunkExecutionState ExecutionState { get; set; } = ChunkExecutionState.Pending;
}
