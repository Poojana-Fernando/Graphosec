using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Performance;

public sealed class CopyPerformanceAdvisor : ICopyPerformanceAdvisor
{
    private const long SmallFileThresholdBytes = 1024 * 1024;
    private const long MediumFileThresholdBytes = 100L * 1024 * 1024;
    private const long LargeFileVerificationThresholdBytes = 1024L * 1024 * 1024;
    private const long VeryLargeFileThresholdBytes = 10L * 1024 * 1024 * 1024;

    public CopyOptions ResolveOptions(long fileSizeBytes, CopyOptions requestedOptions)
    {
        ArgumentNullException.ThrowIfNull(requestedOptions);

        if (!requestedOptions.UseAdaptivePerformance)
        {
            return requestedOptions;
        }

        var recommended = Recommend(fileSizeBytes);
        var chunkSize = Select(requestedOptions.ChunkSize, CopyOptions.DefaultChunkSize, recommended.ChunkSize);
        var maximumWorkers = Select(
            requestedOptions.MaximumWorkers,
            CopyOptions.DefaultMaximumWorkers,
            recommended.MaximumWorkers);
        var maximumQueuedChunks = Select(
            requestedOptions.MaximumQueuedChunks,
            CopyOptions.DefaultMaximumQueuedChunks,
            recommended.MaximumQueuedChunks);

        if (maximumQueuedChunks < maximumWorkers)
        {
            maximumQueuedChunks = maximumWorkers * 2;
        }

        var verifyWholeFile = requestedOptions.VerifyWholeFileAfterCopy;
        if (fileSizeBytes > LargeFileVerificationThresholdBytes)
        {
            verifyWholeFile = false;
        }

        return new CopyOptions
        {
            ChunkSize = chunkSize,
            MaximumWorkers = maximumWorkers,
            MaximumQueuedChunks = maximumQueuedChunks,
            MaximumChunkAttempts = requestedOptions.MaximumChunkAttempts,
            RetryDelayMilliseconds = requestedOptions.RetryDelayMilliseconds,
            IoBufferSize = Select(requestedOptions.IoBufferSize, CopyOptions.DefaultIoBufferSize, recommended.IoBufferSize),
            OverwriteExisting = requestedOptions.OverwriteExisting,
            VerifyWholeFileAfterCopy = verifyWholeFile,
            FlushEveryChunk = requestedOptions.FlushEveryChunk,
            FlushIntervalBytes = requestedOptions.FlushIntervalBytes > 0
                ? requestedOptions.FlushIntervalBytes
                : CopyOptions.DefaultFlushIntervalBytes,
            UseAdaptivePerformance = requestedOptions.UseAdaptivePerformance
        };
    }

    internal static RecommendedPerformanceSettings Recommend(long fileSizeBytes)
    {
        if (fileSizeBytes <= SmallFileThresholdBytes)
        {
            return new RecommendedPerformanceSettings(
                ChunkSize: 256 * 1024,
                MaximumWorkers: 1,
                MaximumQueuedChunks: 2,
                IoBufferSize: 64 * 1024);
        }

        if (fileSizeBytes <= MediumFileThresholdBytes)
        {
            return new RecommendedPerformanceSettings(
                ChunkSize: CopyOptions.DefaultChunkSize,
                MaximumWorkers: Math.Min(2, Environment.ProcessorCount),
                MaximumQueuedChunks: 4,
                IoBufferSize: CopyOptions.DefaultIoBufferSize);
        }

        var workers = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        if (fileSizeBytes >= VeryLargeFileThresholdBytes)
        {
            return new RecommendedPerformanceSettings(
                ChunkSize: 32 * 1024 * 1024,
                MaximumWorkers: workers,
                MaximumQueuedChunks: workers * 2,
                IoBufferSize: 256 * 1024);
        }

        return new RecommendedPerformanceSettings(
            ChunkSize: 8 * 1024 * 1024,
            MaximumWorkers: workers,
            MaximumQueuedChunks: workers * 2,
            IoBufferSize: 128 * 1024);
    }

    private static int Select(int requestedValue, int defaultValue, int recommendedValue) =>
        requestedValue == defaultValue ? recommendedValue : requestedValue;

    internal readonly record struct RecommendedPerformanceSettings(
        int ChunkSize,
        int MaximumWorkers,
        int MaximumQueuedChunks,
        int IoBufferSize);
}
