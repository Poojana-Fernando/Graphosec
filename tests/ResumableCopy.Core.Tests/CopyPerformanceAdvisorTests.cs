using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Performance;

namespace ResumableCopy.Core.Tests;

public class CopyPerformanceAdvisorTests
{
    private readonly CopyPerformanceAdvisor _advisor = new();

    [Fact]
    public void ResolveOptions_WhenAdaptiveDisabled_PreservesRequestedValues()
    {
        var requested = new CopyOptions
        {
            ChunkSize = 1024,
            MaximumWorkers = 3,
            MaximumQueuedChunks = 6,
            IoBufferSize = 4096,
            UseAdaptivePerformance = false
        };

        var resolved = _advisor.ResolveOptions(512 * 1024 * 1024, requested);

        Assert.Equal(requested.ChunkSize, resolved.ChunkSize);
        Assert.Equal(requested.MaximumWorkers, resolved.MaximumWorkers);
        Assert.Equal(requested.MaximumQueuedChunks, resolved.MaximumQueuedChunks);
        Assert.Equal(requested.IoBufferSize, resolved.IoBufferSize);
    }

    [Fact]
    public void ResolveOptions_ForSmallFile_UsesSingleWorkerAndSmallerChunk()
    {
        var resolved = _advisor.ResolveOptions(512 * 1024, new CopyOptions());

        Assert.Equal(256 * 1024, resolved.ChunkSize);
        Assert.Equal(1, resolved.MaximumWorkers);
        Assert.Equal(2, resolved.MaximumQueuedChunks);
        Assert.Equal(64 * 1024, resolved.IoBufferSize);
    }

    [Fact]
    public void ResolveOptions_ForLargeFile_UsesParallelWorkers()
    {
        var resolved = _advisor.ResolveOptions(256L * 1024 * 1024, new CopyOptions());

        Assert.Equal(8 * 1024 * 1024, resolved.ChunkSize);
        Assert.InRange(resolved.MaximumWorkers, 2, 4);
        Assert.Equal(resolved.MaximumWorkers * 2, resolved.MaximumQueuedChunks);
        Assert.Equal(128 * 1024, resolved.IoBufferSize);
    }

    [Fact]
    public void ResolveOptions_ForVeryLargeFile_UsesLargerChunks()
    {
        var resolved = _advisor.ResolveOptions(20L * 1024 * 1024 * 1024, new CopyOptions());

        Assert.Equal(32 * 1024 * 1024, resolved.ChunkSize);
        Assert.Equal(256 * 1024, resolved.IoBufferSize);
        Assert.False(resolved.VerifyWholeFileAfterCopy);
    }

    [Fact]
    public void ResolveOptions_ForOneGigabyteFile_KeepsWholeFileVerification()
    {
        var resolved = _advisor.ResolveOptions(1024L * 1024 * 1024, new CopyOptions { VerifyWholeFileAfterCopy = true });

        Assert.True(resolved.VerifyWholeFileAfterCopy);
    }

    [Fact]
    public void ResolveOptions_ForLargeFileOverOneGigabyte_SkipsWholeFileVerification()
    {
        var resolved = _advisor.ResolveOptions(1024L * 1024 * 1024 + 1, new CopyOptions { VerifyWholeFileAfterCopy = true });

        Assert.False(resolved.VerifyWholeFileAfterCopy);
    }

    [Fact]
    public void ResolveOptions_PreservesExplicitOverridesWhenAdaptiveDisabled()
    {
        var requested = new CopyOptions
        {
            ChunkSize = 16 * 1024,
            MaximumWorkers = 1,
            MaximumQueuedChunks = 2,
            IoBufferSize = 32 * 1024,
            UseAdaptivePerformance = false
        };

        var resolved = _advisor.ResolveOptions(256L * 1024 * 1024, requested);

        Assert.Equal(16 * 1024, resolved.ChunkSize);
        Assert.Equal(1, resolved.MaximumWorkers);
        Assert.Equal(2, resolved.MaximumQueuedChunks);
        Assert.Equal(32 * 1024, resolved.IoBufferSize);
    }

    [Fact]
    public void ResolveOptions_PreservesNonDefaultChunkSizeWithAdaptiveEnabled()
    {
        var requested = new CopyOptions
        {
            ChunkSize = 16 * 1024
        };

        var resolved = _advisor.ResolveOptions(256L * 1024 * 1024, requested);

        Assert.Equal(16 * 1024, resolved.ChunkSize);
    }
}
