using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Core;

public static class ChunkPlanMerger
{
    public static List<ChunkRecord> Merge(CopySession session, IReadOnlyList<ChunkRecord> persistedChunks)
    {
        ArgumentNullException.ThrowIfNull(session);

        var plan = ChunkPlanner.CreateChunks(session.SourceIdentity.Length, session.ChunkSize);
        if (persistedChunks.Count == 0)
        {
            return plan.Select(ClonePending).ToList();
        }

        var persistedByIndex = persistedChunks.ToDictionary(static chunk => chunk.Index);
        var merged = new List<ChunkRecord>(plan.Count);

        foreach (var planned in plan)
        {
            if (!persistedByIndex.TryGetValue(planned.Index, out var persisted))
            {
                merged.Add(ClonePending(planned));
                continue;
            }

            merged.Add(new ChunkRecord
            {
                Index = planned.Index,
                Offset = planned.Offset,
                Length = planned.Length,
                Hash = persisted.Hash is null ? null : (byte[])persisted.Hash.Clone(),
                IsComplete = persisted.IsComplete,
                ExecutionState = persisted.ExecutionState
            });
        }

        return merged;
    }

    private static ChunkRecord ClonePending(ChunkRecord chunk) =>
        new()
        {
            Index = chunk.Index,
            Offset = chunk.Offset,
            Length = chunk.Length,
            IsComplete = false,
            ExecutionState = ChunkExecutionState.Pending
        };
}
