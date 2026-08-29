namespace ResumableCopy.Core.Domain;

public sealed class CopyOptions
{
    public const int DefaultChunkSize = 4 * 1024 * 1024;

    public const int DefaultMaximumWorkers = 1;

    public const int DefaultMaximumQueuedChunks = 4;

    public const int DefaultMaximumChunkAttempts = 2;

    public const int DefaultRetryDelayMilliseconds = 50;

    public const int DefaultIoBufferSize = 81920;

    public const long DefaultFlushIntervalBytes = 64L * 1024 * 1024;

    public int ChunkSize { get; init; } = DefaultChunkSize;

    public int MaximumWorkers { get; init; } = DefaultMaximumWorkers;

    public int MaximumQueuedChunks { get; init; } = DefaultMaximumQueuedChunks;

    public int MaximumChunkAttempts { get; init; } = DefaultMaximumChunkAttempts;

    public int RetryDelayMilliseconds { get; init; } = DefaultRetryDelayMilliseconds;

    public int IoBufferSize { get; init; } = DefaultIoBufferSize;

    public bool OverwriteExisting { get; init; }

    public bool VerifyWholeFileAfterCopy { get; init; } = true;

    public bool FlushEveryChunk { get; init; } = true;

    public long FlushIntervalBytes { get; init; } = DefaultFlushIntervalBytes;

    public bool UseAdaptivePerformance { get; init; } = true;
}
