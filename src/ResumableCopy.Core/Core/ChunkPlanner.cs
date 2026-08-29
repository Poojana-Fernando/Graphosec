using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Core;

public static class ChunkPlanner
{
    public static IReadOnlyList<ChunkRecord> CreateChunks(long fileLength, int chunkSize)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
        }

        if (fileLength == 0)
        {
            return Array.Empty<ChunkRecord>();
        }

        var totalChunks = (int)((fileLength + chunkSize - 1) / chunkSize);
        var chunks = new List<ChunkRecord>(totalChunks);

        for (var index = 0; index < totalChunks; index++)
        {
            var offset = (long)index * chunkSize;
            var length = (int)Math.Min(chunkSize, fileLength - offset);
            chunks.Add(new ChunkRecord
            {
                Index = index,
                Offset = offset,
                Length = length,
                IsComplete = false
            });
        }

        return chunks;
    }

    public static int CalculateTotalChunks(long fileLength, int chunkSize)
    {
        if (fileLength == 0)
        {
            return 0;
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
        }

        return (int)((fileLength + chunkSize - 1) / chunkSize);
    }
}
