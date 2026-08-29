using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class DeviceMonitoringTests
{
    [Fact]
    public async Task CopyAsync_WhenDestinationVolumeDisconnects_PersistsWaitingForDestination()
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
                context.DeviceMonitor.SetPathInaccessible(context.StagingLocator.GetCacheDirectory(destinationPath));
            }
        });

        var exception = await Assert.ThrowsAsync<DestinationUnavailableException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 8 * 1024 }),
                progress,
                CancellationToken.None));

        Assert.NotNull(sessionId);
        Assert.Contains("Destination path is not accessible", exception.Message);

        var cacheDirectory = context.StagingLocator.GetCacheDirectory(destinationPath);
        using var repository = new SqliteSessionRepository(cacheDirectory);
        var session = await repository.FindAsync(sessionId!, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(CopyState.WaitingForDestination, session!.State);
    }

    [Fact]
    public async Task CopyAsync_WhenSourceBecomesUnavailable_PersistsWaitingForSource()
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
                context.FileSystem.HidePath(sourcePath);
            }
        });

        var exception = await Assert.ThrowsAsync<SourceUnavailableException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 8 * 1024 }),
                progress,
                CancellationToken.None));

        Assert.NotNull(sessionId);
        Assert.Contains("Source file not found", exception.Message);

        var cacheDirectory = context.StagingLocator.GetCacheDirectory(destinationPath);
        using var repository = new SqliteSessionRepository(cacheDirectory);
        var session = await repository.FindAsync(sessionId!, CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal(CopyState.WaitingForSource, session!.State);
    }

    [Fact]
    public async Task ResumeAsync_AfterDestinationReconnects_CompletesTransfer()
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
                context.DeviceMonitor.SetPathInaccessible(context.StagingLocator.GetCacheDirectory(destinationPath));
            }
        });

        await Assert.ThrowsAsync<DestinationUnavailableException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 8 * 1024 }),
                progress,
                CancellationToken.None));

        Assert.NotNull(sessionId);
        context.DeviceMonitor.Reset();

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
    public async Task RecoverSessionAsync_WhenDestinationVolumeUnavailable_ReturnsWaitingForDestination()
    {
        using var temp = new TempDirectory();
        var context = new MonitoringCopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(16 * 1024));

        var sessionId = await InterruptCopy(context, sourcePath, destinationPath, cancelAfterChunks: 1);
        context.DeviceMonitor.SetPathInaccessible(context.StagingLocator.GetCacheDirectory(destinationPath));

        var recovery = await context.RecoveryService.RecoverSessionAsync(destinationPath, sessionId, CancellationToken.None);

        Assert.False(recovery.CanResume);
        Assert.Equal(CopyState.WaitingForDestination, recovery.State);
    }

    [Fact]
    public async Task CopyAsync_WhenDestinationUnavailableAtStart_ThrowsBeforeSessionCreated()
    {
        using var temp = new TempDirectory();
        var context = new MonitoringCopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(8 * 1024));

        var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(destinationDirectory);
        context.DeviceMonitor.SetPathInaccessible(destinationDirectory);

        await Assert.ThrowsAsync<DestinationUnavailableException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 4 * 1024 }),
                progress: null,
                CancellationToken.None));
    }

    private static async Task<string> InterruptCopy(
        MonitoringCopyEngineTestContext context,
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
