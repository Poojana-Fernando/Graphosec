using ResumableCopy.Core.Core;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Integrity;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Storage.Sqlite;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class TransferRecoveryTests
{
    [Fact]
    public async Task RecoverSessionAsync_WithPartiallyCompletedChunks_ResetsInvalidChunks()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        var sourceBytes = CreateDeterministicBytes(32 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        await InterruptCopyAfterChunks(context, sourcePath, destinationPath, chunkSize: 8 * 1024, cancelAfterChunks: 2);

        var cacheDirectory = context.StagingLocator.GetCacheDirectory(destinationPath);
        using (var repository = new SqliteSessionRepository(cacheDirectory))
        {
            var sessions = await repository.FindUnfinishedAsync(CancellationToken.None);
            var session = Assert.Single(sessions);

            var chunk = session.Chunks[0];
            chunk.Hash = Convert.FromHexString("1111111111111111111111111111111111111111111111111111111111111111");
            await repository.MarkChunkCompleteAsync(session.SessionId, chunk, CancellationToken.None);
        }

        var sessionId = (await context.RecoveryService.DiscoverRecoverableSessionsAsync(destinationPath, CancellationToken.None))[0].SessionId;
        var recovery = await context.RecoveryService.RecoverSessionAsync(destinationPath, sessionId, CancellationToken.None);

        Assert.True(recovery.CanResume);
        Assert.Equal(CopyState.Paused, recovery.State);
        Assert.Equal(1, recovery.InvalidatedChunkCount);
    }

    [Fact]
    public async Task RecoverSessionAsync_WithMissingStagingFile_ResetsCompletedChunks()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(32 * 1024));

        var sessionId = await InterruptCopyAfterChunks(context, sourcePath, destinationPath, chunkSize: 8 * 1024, cancelAfterChunks: 2);
        var partPath = Path.Combine(context.StagingLocator.GetCacheDirectory(destinationPath), $"{sessionId}.part");
        File.Delete(partPath);

        var recovery = await context.RecoveryService.RecoverSessionAsync(destinationPath, sessionId, CancellationToken.None);

        Assert.True(recovery.CanResume);
        Assert.True(recovery.InvalidatedChunkCount >= 1);
    }

    [Fact]
    public async Task RecoverSessionAsync_WithChangedSource_ReturnsRecoveryRequired()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(16 * 1024));

        var sessionId = await InterruptCopyAfterChunks(context, sourcePath, destinationPath, chunkSize: 8 * 1024, cancelAfterChunks: 1);
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(20 * 1024));

        var recovery = await context.RecoveryService.RecoverSessionAsync(destinationPath, sessionId, CancellationToken.None);

        Assert.False(recovery.CanResume);
        Assert.Equal(CopyState.RecoveryRequired, recovery.State);
    }

    [Fact]
    public async Task RecoverSessionAsync_WithMissingSource_ReturnsWaitingForSource()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(16 * 1024));

        var sessionId = await InterruptCopyAfterChunks(context, sourcePath, destinationPath, chunkSize: 8 * 1024, cancelAfterChunks: 1);
        File.Delete(sourcePath);

        var recovery = await context.RecoveryService.RecoverSessionAsync(destinationPath, sessionId, CancellationToken.None);

        Assert.False(recovery.CanResume);
        Assert.Equal(CopyState.WaitingForSource, recovery.State);
    }

    [Fact]
    public async Task RecoverUnfinishedSessionsAsync_IsIdempotent()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(24 * 1024));

        await InterruptCopyAfterChunks(context, sourcePath, destinationPath, chunkSize: 8 * 1024, cancelAfterChunks: 1);

        var first = await context.RecoveryService.RecoverUnfinishedSessionsAsync(destinationPath, CancellationToken.None);
        var second = await context.RecoveryService.RecoverUnfinishedSessionsAsync(destinationPath, CancellationToken.None);

        Assert.Single(first);
        Assert.Single(second);
        Assert.True(first[0].CanResume);
        Assert.True(second[0].CanResume);
        Assert.Equal(0, second[0].InvalidatedChunkCount);
    }

    [Fact]
    public async Task DiscoverRecoverableSessionsAsync_WhenVolumeNotReady_ThrowsDestinationUnavailable()
    {
        using var temp = new TempDirectory();
        var context = new MonitoringCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");

        context.DeviceMonitor.SetVolumeNotReady(destinationPath);

        var exception = await Assert.ThrowsAsync<DestinationUnavailableException>(() =>
            context.RecoveryService.DiscoverRecoverableSessionsAsync(destinationPath, CancellationToken.None).AsTask());

        Assert.Contains("not ready", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscoverRecoverableSessionsAsync_ReturnsUnfinishedSessions()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(24 * 1024));

        var sessionId = await InterruptCopyAfterChunks(context, sourcePath, destinationPath, chunkSize: 8 * 1024, cancelAfterChunks: 1);

        var discovered = await context.RecoveryService.DiscoverRecoverableSessionsAsync(destinationPath, CancellationToken.None);

        Assert.Single(discovered);
        Assert.Equal(sessionId, discovered[0].SessionId);
    }

    [Fact]
    public async Task RecoverSessionAsync_WithMissingSession_ReturnsFailure()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");

        var recovery = await context.RecoveryService.RecoverSessionAsync(destinationPath, "missing", CancellationToken.None);

        Assert.False(recovery.CanResume);
        Assert.Equal(CopyState.Failed, recovery.State);
    }

    [Fact]
    public async Task RecoverSessionAsync_AfterRepositoryRecreation_PreservesRecovery()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(16 * 1024));

        var sessionId = await InterruptCopyAfterChunks(context, sourcePath, destinationPath, chunkSize: 8 * 1024, cancelAfterChunks: 1);

        var rediscoveredContext = new SqliteCopyEngineTestContext();
        var recovery = await rediscoveredContext.RecoveryService.RecoverSessionAsync(destinationPath, sessionId, CancellationToken.None);

        Assert.True(recovery.CanResume);
    }

    private static async Task<string> InterruptCopyAfterChunks(
        SqliteCopyEngineTestContext context,
        string sourcePath,
        string destinationPath,
        int chunkSize,
        int cancelAfterChunks)
    {
        using var cancellationSource = new CancellationTokenSource();
        string? sessionId = null;

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
                new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = chunkSize }),
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
