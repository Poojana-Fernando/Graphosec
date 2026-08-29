using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Reliability;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class CrashConsistencyTests
{
    [Fact]
    public async Task CopyAsync_AfterDatabaseCommitFailure_ThenResume_CompletesSuccessfully()
    {
        using var temp = new TempDirectory();
        var faultInjector = new DeterministicFaultInjector(new FaultRule
        {
            Point = FaultPoint.BeforeDatabaseCommit,
            Kind = FaultKind.DatabaseFailure,
            ChunkIndex = 1,
            Occurrence = 1
        });

        var context = new FaultInjectionTestContext(faultInjector);
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourceBytes = CreateDeterministicBytes(32 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

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

        var result = await context.Engine.ResumeAsync(
            sessionId!,
            destinationPath,
            new CopyOptions { ChunkSize = 8 * 1024 },
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task CopyAsync_AfterWriteFailureMidTransfer_ThenResume_CompletesSuccessfully()
    {
        using var temp = new TempDirectory();
        var faultInjector = new DeterministicFaultInjector(new FaultRule
        {
            Point = FaultPoint.BeforeChunkWrite,
            Kind = FaultKind.WriteFailure,
            ChunkIndex = 2,
            Occurrence = 1
        });

        var context = new FaultInjectionTestContext(faultInjector);
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourceBytes = CreateDeterministicBytes(48 * 1024);
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

        var result = await context.Engine.ResumeAsync(
            sessionId!,
            destinationPath,
            new CopyOptions { ChunkSize = 8 * 1024 },
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task CopyAsync_AfterFinalizationFailure_ThenResume_CompletesSuccessfully()
    {
        using var temp = new TempDirectory();
        var faultInjector = new DeterministicFaultInjector(new FaultRule
        {
            Point = FaultPoint.BeforeFinalization,
            Kind = FaultKind.WriteFailure,
            Occurrence = 1
        });

        var context = new FaultInjectionTestContext(faultInjector);
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

        var result = await context.Engine.ResumeAsync(
            sessionId!,
            destinationPath,
            new CopyOptions { ChunkSize = 8 * 1024 },
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task Recovery_AfterTamperedStagingFile_InvalidatesAffectedChunks()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourceBytes = CreateDeterministicBytes(32 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var sessionId = await InterruptCopy(context, sourcePath, destinationPath, cancelAfterChunks: 3);

        var partPath = Path.Combine(context.StagingLocator.GetCacheDirectory(destinationPath), $"{sessionId}.part");
        using (var stream = new FileStream(partPath, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Seek(0, SeekOrigin.Begin);
            stream.WriteByte(0xFE);
        }

        var recovery = await context.RecoveryService.RecoverSessionAsync(destinationPath, sessionId, CancellationToken.None);

        Assert.True(recovery.CanResume);
        Assert.True(recovery.InvalidatedChunkCount >= 1);
    }

    private static async Task<string> InterruptCopy(
        SqliteCopyEngineTestContext context,
        string sourcePath,
        string destinationPath,
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
                new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 8 * 1024 }),
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
