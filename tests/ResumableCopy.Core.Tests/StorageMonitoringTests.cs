using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class StorageMonitoringTests
{
    [Fact]
    public async Task CopyAsync_WhenInsufficientSpaceAtStart_ThrowsBeforeSessionCreated()
    {
        using var temp = new TempDirectory();
        var context = new MonitoringCopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(64 * 1024));

        context.FileSystem.SetFreeSpace(16 * 1024);

        var exception = await Assert.ThrowsAsync<InsufficientStorageException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 8 * 1024 }),
                progress: null,
                CancellationToken.None));

        Assert.Contains("Insufficient storage", exception.Message);
    }

    [Fact]
    public async Task CopyAsync_WhenSpaceRunsOutDuringTransfer_PersistsWaitingForStorage()
    {
        using var temp = new TempDirectory();
        var context = new MonitoringCopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(48 * 1024));

        string? sessionId = null;
        var progress = new Progress<CopyProgress>(report =>
        {
            sessionId ??= report.SessionId;

            if (report.CompletedChunks >= 1)
            {
                context.FileSystem.SetFreeSpace(4 * 1024);
            }
        });

        var exception = await Assert.ThrowsAsync<InsufficientStorageException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 8 * 1024 }),
                progress,
                CancellationToken.None));

        Assert.NotNull(sessionId);
        Assert.Contains("Insufficient storage", exception.Message);

        var cacheDirectory = context.StagingLocator.GetCacheDirectory(destinationPath);
        using var repository = new SqliteSessionRepository(cacheDirectory);
        var session = await repository.FindAsync(sessionId!, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(CopyState.WaitingForStorage, session!.State);
    }

    [Fact]
    public async Task ResumeAsync_AfterSpaceBecomesAvailable_CompletesTransfer()
    {
        using var temp = new TempDirectory();
        var context = new MonitoringCopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourceBytes = CreateDeterministicBytes(32 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        string? sessionId = null;
        var progress = new Progress<CopyProgress>(report =>
        {
            sessionId ??= report.SessionId;

            if (report.CompletedChunks >= 1)
            {
                context.FileSystem.SetFreeSpace(4 * 1024);
            }
        });

        await Assert.ThrowsAsync<InsufficientStorageException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 8 * 1024 }),
                progress,
                CancellationToken.None));

        Assert.NotNull(sessionId);
        context.FileSystem.ClearFreeSpaceOverride();

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
    public void CopyStateMapper_MapsSourceChangedToRecoveryRequired()
    {
        var state = CopyStateMapper.ResolveWaitingState(new SourceChangedException("Source file changed during transfer."));

        Assert.Equal(CopyState.RecoveryRequired, state);
    }

    [Fact]
    public async Task RecoverSessionAsync_WithChangedSource_ReturnsRecoveryRequired()
    {
        using var temp = new TempDirectory();
        var context = new MonitoringCopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(16 * 1024));

        string? sessionId = null;
        using var cancellationSource = new CancellationTokenSource();
        var progress = new Progress<CopyProgress>(report =>
        {
            sessionId ??= report.SessionId;

            if (report.CompletedChunks >= 1)
            {
                cancellationSource.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 8 * 1024 }),
                progress,
                cancellationSource.Token));

        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(20 * 1024));

        var recovery = await context.RecoveryService.RecoverSessionAsync(destinationPath, sessionId!, CancellationToken.None);

        Assert.False(recovery.CanResume);
        Assert.Equal(CopyState.RecoveryRequired, recovery.State);
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
