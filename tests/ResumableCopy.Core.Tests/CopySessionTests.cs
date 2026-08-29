using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Tests;

public class CopySessionTests
{
    [Fact]
    public void CompletedBytes_DoesNotOverflow_WhenCompletedBytesExceedTwoGigabytes()
    {
        const long chunkSize = 4L * 1024 * 1024;
        const int completedChunks = 600;
        var expectedBytes = completedChunks * chunkSize;

        var session = new CopySession
        {
            SessionId = "large-file",
            SourcePath = @"C:\source.bin",
            DestinationPath = @"D:\dest.bin",
            SourceIdentity = new SourceIdentity(expectedBytes, DateTime.UtcNow, DateTime.UtcNow),
            StagingPath = @"D:\dest.bin.part",
            ChunkSize = (int)chunkSize,
            TotalChunks = completedChunks,
            Chunks = Enumerable.Range(0, completedChunks)
                .Select(index => new ChunkRecord
                {
                    Index = index,
                    Offset = index * chunkSize,
                    Length = (int)chunkSize,
                    IsComplete = true
                })
                .ToList()
        };

        var completedBytes = session.CompletedBytes;

        Assert.True(completedBytes > 2_000_000_000L);
        Assert.Equal(expectedBytes, completedBytes);
    }
}
