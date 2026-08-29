using ResumableCopy.Core.Core;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Tests;

public class ChunkTests
{
    [Fact]
    public void CreateChunks_EmptyFile_ReturnsZeroChunks()
    {
        var chunks = ChunkPlanner.CreateChunks(0, 1024);
        Assert.Empty(chunks);
    }

    [Fact]
    public void CreateChunks_SmallerThanChunkSize_ReturnsSinglePartialChunk()
    {
        var chunks = ChunkPlanner.CreateChunks(512, 1024);

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].Index);
        Assert.Equal(0, chunks[0].Offset);
        Assert.Equal(512, chunks[0].Length);
    }

    [Fact]
    public void CreateChunks_ExactMultiple_ReturnsEqualSizedChunks()
    {
        var chunks = ChunkPlanner.CreateChunks(2048, 1024);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(1024, chunks[0].Length);
        Assert.Equal(1024, chunks[1].Length);
        Assert.Equal(1024, chunks[1].Offset);
    }

    [Fact]
    public void CreateChunks_WithRemainder_LastChunkIsPartial()
    {
        var chunks = ChunkPlanner.CreateChunks(2500, 1024);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(452, chunks[2].Length);
        Assert.Equal(2048, chunks[2].Offset);
    }

    [Fact]
    public void CalculateTotalChunks_MatchesCreateChunksCount()
    {
        Assert.Equal(0, ChunkPlanner.CalculateTotalChunks(0, 1024));
        Assert.Equal(3, ChunkPlanner.CalculateTotalChunks(2500, 1024));
    }
}
