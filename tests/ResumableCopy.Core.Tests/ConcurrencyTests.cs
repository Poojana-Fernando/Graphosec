using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Storage.Sqlite;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class ConcurrencyTests
{
    [Fact]
    public async Task CopyAsync_WithMultipleWorkers_ProducesExactBytes()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourceBytes = CreateDeterministicBytes(128 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var result = await context.Engine.CopyAsync(
            new CopyJob(sourcePath, destinationPath, CreateParallelOptions(chunkSize: 8 * 1024, workers: 4)),
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task CopyAsync_WithSingleWorker_MatchesDefaultBehavior()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourceBytes = CreateDeterministicBytes(32 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var result = await context.Engine.CopyAsync(
            new CopyJob(sourcePath, destinationPath, new CopyOptions
            {
                ChunkSize = 8 * 1024,
                MaximumWorkers = 1
            }),
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task CopyAsync_WithManyChunksAndLimitedQueue_CompletesSuccessfully()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourceBytes = CreateDeterministicBytes(256 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var result = await context.Engine.CopyAsync(
            new CopyJob(sourcePath, destinationPath, new CopyOptions
            {
                ChunkSize = 4 * 1024,
                MaximumWorkers = 4,
                MaximumQueuedChunks = 4
            }),
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task CopyAsync_WhenCancelledDuringParallelCopy_LeavesRecoverableSession()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(256 * 1024));

        using var cancellationSource = new CancellationTokenSource();
        string? sessionId = null;

        var progress = new Progress<CopyProgress>(report =>
        {
            sessionId ??= report.SessionId;

            if (report.CompletedChunks >= 2)
            {
                cancellationSource.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, CreateParallelOptions(chunkSize: 8 * 1024, workers: 4)),
                progress,
                cancellationSource.Token));

        Assert.NotNull(sessionId);

        var cacheDirectory = context.StagingLocator.GetCacheDirectory(destinationPath);
        using var repository = new SqliteSessionRepository(cacheDirectory);
        var session = await repository.FindAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal(CopyState.Paused, session!.State);
        Assert.InRange(session.CompletedChunkCount, 1, session.TotalChunks - 1);
    }

    [Fact]
    public async Task ResumeAsync_AfterParallelInterruption_CompletesWithMultipleWorkers()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourceBytes = CreateDeterministicBytes(96 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var options = CreateParallelOptions(chunkSize: 8 * 1024, workers: 4);
        var sessionId = await InterruptParallelCopy(context, sourcePath, destinationPath, options, cancelAfterChunks: 3);

        var result = await context.Engine.ResumeAsync(
            sessionId,
            destinationPath,
            options,
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task CopyAsync_RejectsQueueSmallerThanWorkers()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(16 * 1024));

        var options = new CopyOptions
        {
            ChunkSize = 4096,
            MaximumWorkers = 4,
            MaximumQueuedChunks = 2
        };

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, options),
                progress: null,
                CancellationToken.None));

        Assert.Contains("Maximum queued chunks", exception.Message);
    }

    [Fact]
    public async Task CopyAsync_WithMultipleWorkers_RecordsCompletedChunksInDatabase()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(128 * 1024));

        var options = CreateParallelOptions(chunkSize: 8 * 1024, workers: 4);
        var sessionId = await InterruptParallelCopy(context, sourcePath, destinationPath, options, cancelAfterChunks: 4);

        var cacheDirectory = context.StagingLocator.GetCacheDirectory(destinationPath);
        using var repository = new SqliteSessionRepository(cacheDirectory);
        var session = await repository.FindAsync(sessionId, CancellationToken.None);

        Assert.NotNull(session);
        Assert.InRange(session!.CompletedChunkCount, 1, session.TotalChunks);
        Assert.All(session.Chunks.Where(static chunk => chunk.IsComplete), chunk => Assert.NotNull(chunk.Hash));
    }

    private static CopyOptions CreateParallelOptions(int chunkSize, int workers) =>
        new()
        {
            ChunkSize = chunkSize,
            MaximumWorkers = workers,
            MaximumQueuedChunks = workers * 2
        };

    private static async Task<string> InterruptParallelCopy(
        SqliteCopyEngineTestContext context,
        string sourcePath,
        string destinationPath,
        CopyOptions options,
        int cancelAfterChunks)
    {
        string? sessionId = null;
        using var cancellationSource = new CancellationTokenSource();
        var progress = new Progress<CopyProgress>(report =>
        {
            sessionId ??= report.SessionId;

            if (report.CompletedChunks >= cancelAfterChunks)
            {
                cancellationSource.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, options),
                progress,
                cancellationSource.Token));

        return sessionId ?? throw new InvalidOperationException("Session id was not reported.");
    }

    private static byte[] CreateDeterministicBytes(int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < length; index++)
        {
            bytes[index] = (byte)(index % 251);
        }

        return bytes;
    }
}
