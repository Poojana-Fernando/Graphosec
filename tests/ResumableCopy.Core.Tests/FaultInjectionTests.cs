using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Reliability;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class FaultInjectionTests
{
    [Fact]
    public async Task CopyAsync_WhenChunkWriteFails_DoesNotMarkChunkVerifiedInDatabase()
    {
        using var temp = new TempDirectory();
        var context = new FaultInjectionTestContext(new FaultRule
        {
            Point = FaultPoint.BeforeChunkWrite,
            Kind = FaultKind.WriteFailure,
            ChunkIndex = 1
        });

        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(32 * 1024));

        string? sessionId = null;
        var progress = new Progress<CopyProgress>(report => sessionId ??= report.SessionId);

        await Assert.ThrowsAnyAsync<CopyException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions
                {
                    ChunkSize = 8 * 1024,
                    MaximumChunkAttempts = 1
                }),
                progress,
                CancellationToken.None));

        Assert.NotNull(sessionId);

        var cacheDirectory = context.StagingLocator.GetCacheDirectory(destinationPath);
        using var repository = new SqliteSessionRepository(cacheDirectory);
        var session = await repository.FindAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(session);
        Assert.True(session!.Chunks[0].IsComplete);
        Assert.False(session.Chunks[1].IsComplete);
    }

    [Fact]
    public async Task CopyAsync_WhenDatabaseCommitFails_DoesNotPersistVerifiedChunk()
    {
        using var temp = new TempDirectory();
        var context = new FaultInjectionTestContext(new FaultRule
        {
            Point = FaultPoint.BeforeDatabaseCommit,
            Kind = FaultKind.DatabaseFailure,
            ChunkIndex = 1
        });

        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(32 * 1024));

        string? sessionId = null;
        var progress = new Progress<CopyProgress>(report => sessionId ??= report.SessionId);

        await Assert.ThrowsAsync<SessionPersistenceException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions
                {
                    ChunkSize = 8 * 1024,
                    MaximumChunkAttempts = 1
                }),
                progress,
                CancellationToken.None));

        Assert.NotNull(sessionId);

        var cacheDirectory = context.StagingLocator.GetCacheDirectory(destinationPath);
        using var repository = new SqliteSessionRepository(cacheDirectory);
        var session = await repository.FindAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(session);
        Assert.True(session!.Chunks[0].IsComplete);
        Assert.False(session.Chunks[1].IsComplete);
    }

    [Fact]
    public async Task CopyAsync_WhenChunkReadFails_DoesNotAdvancePastFailedChunk()
    {
        using var temp = new TempDirectory();
        var context = new FaultInjectionTestContext(new FaultRule
        {
            Point = FaultPoint.BeforeChunkRead,
            Kind = FaultKind.ReadFailure,
            ChunkIndex = 0
        });

        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(16 * 1024));

        await Assert.ThrowsAnyAsync<CopyException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions
                {
                    ChunkSize = 8 * 1024,
                    MaximumChunkAttempts = 1
                }),
                progress: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task CopyAsync_WhenFinalizationFails_LeavesRecoverableSessionWithoutDestinationFile()
    {
        using var temp = new TempDirectory();
        var context = new FaultInjectionTestContext(new FaultRule
        {
            Point = FaultPoint.BeforeFinalization,
            Kind = FaultKind.WriteFailure
        });

        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourceBytes = CreateDeterministicBytes(16 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        string? sessionId = null;
        var progress = new Progress<CopyProgress>(report => sessionId ??= report.SessionId);

        await Assert.ThrowsAnyAsync<CopyException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions
                {
                    ChunkSize = 8 * 1024,
                    MaximumChunkAttempts = 1
                }),
                progress,
                CancellationToken.None));

        Assert.NotNull(sessionId);
        Assert.False(File.Exists(destinationPath));

        var cacheDirectory = context.StagingLocator.GetCacheDirectory(destinationPath);
        Assert.True(File.Exists(Path.Combine(cacheDirectory, $"{sessionId}.part")));
    }

    [Fact]
    public async Task CopyAsync_WhenBytesCorruptedAfterRead_FailsVerificationWithoutPersistingChunk()
    {
        using var temp = new TempDirectory();
        var context = new FaultInjectionTestContext(new FaultRule
        {
            Point = FaultPoint.BeforeChunkVerify,
            Kind = FaultKind.CorruptBytes,
            ChunkIndex = 0,
            CorruptByteOffset = 0
        });

        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(16 * 1024));

        string? sessionId = null;
        var progress = new Progress<CopyProgress>(report => sessionId ??= report.SessionId);

        await Assert.ThrowsAsync<IntegrityException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions
                {
                    ChunkSize = 8 * 1024,
                    MaximumChunkAttempts = 1
                }),
                progress,
                CancellationToken.None));

        Assert.NotNull(sessionId);

        var cacheDirectory = context.StagingLocator.GetCacheDirectory(destinationPath);
        using var repository = new SqliteSessionRepository(cacheDirectory);
        var session = await repository.FindAsync(sessionId!, CancellationToken.None);

        Assert.NotNull(session);
        Assert.False(session!.Chunks[0].IsComplete);
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
