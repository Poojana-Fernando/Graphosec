using ResumableCopy.Core.Core;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Tests;

public class ChunkPlanMergerTests
{
    [Fact]
    public void Merge_WithNoPersistedChunks_ReturnsFullPlan()
    {
        var session = CreateSession(totalBytes: 2048, chunkSize: 512);
        var merged = ChunkPlanMerger.Merge(session, []);

        Assert.Equal(4, merged.Count);
        Assert.All(merged, chunk => Assert.False(chunk.IsComplete));
    }

    [Fact]
    public void Merge_WithPersistedVerifiedChunk_MarksOnlyThatChunkComplete()
    {
        var session = CreateSession(totalBytes: 2048, chunkSize: 512);
        var persisted =
            new[]
            {
                new ChunkRecord
                {
                    Index = 1,
                    Offset = 512,
                    Length = 512,
                    Hash = [1, 2, 3],
                    IsComplete = true
                }
            };

        var merged = ChunkPlanMerger.Merge(session, persisted);

        Assert.Equal(4, merged.Count);
        Assert.False(merged[0].IsComplete);
        Assert.True(merged[1].IsComplete);
        Assert.Equal([1, 2, 3], merged[1].Hash);
        Assert.False(merged[2].IsComplete);
    }

    private static CopySession CreateSession(long totalBytes, int chunkSize) =>
        new()
        {
            SessionId = "session-1",
            SourcePath = @"C:\source.bin",
            DestinationPath = @"D:\dest.bin",
            StagingPath = @"D:\dest.bin.part",
            SourceIdentity = new SourceIdentity(totalBytes, DateTime.UtcNow, DateTime.UtcNow),
            ChunkSize = chunkSize,
            TotalChunks = ChunkPlanner.CalculateTotalChunks(totalBytes, chunkSize),
            Chunks = []
        };
}
