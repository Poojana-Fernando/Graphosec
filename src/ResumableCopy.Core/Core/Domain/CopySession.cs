namespace ResumableCopy.Core.Domain;

public sealed class CopySession
{
    public required string SessionId { get; init; }

    public required string SourcePath { get; init; }

    public required string DestinationPath { get; init; }

    public required SourceIdentity SourceIdentity { get; set; }

    public required string StagingPath { get; set; }

    public required int ChunkSize { get; init; }

    public required int TotalChunks { get; init; }

    public List<ChunkRecord> Chunks { get; set; } = [];

    public CopyState State { get; set; } = CopyState.Pending;

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? LastError { get; set; }

    public int CompletedChunkCount => Chunks.Count(static chunk => chunk.IsComplete);

    public long CompletedBytes => Chunks
        .Where(static chunk => chunk.IsComplete)
        .Sum(static chunk => (long)chunk.Length);
}
