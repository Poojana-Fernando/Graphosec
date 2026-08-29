using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Concurrency;

internal sealed class ChunkWorkCoordinator
{
    private readonly object _sync = new();

    public bool TryBegin(ChunkRecord chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        lock (_sync)
        {
            if (chunk.IsComplete || chunk.ExecutionState == ChunkExecutionState.InProgress)
            {
                return false;
            }

            chunk.ExecutionState = ChunkExecutionState.InProgress;
            return true;
        }
    }

    public void MarkCompleted(ChunkRecord chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        lock (_sync)
        {
            chunk.ExecutionState = ChunkExecutionState.Completed;
        }
    }

    public void ResetToPending(ChunkRecord chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        lock (_sync)
        {
            chunk.ExecutionState = ChunkExecutionState.Pending;
        }
    }
}
