using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class ResumeTests
{
    [Fact]
    public async Task ResumeAsync_AfterInterruption_CompletesCopy()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        var sourceBytes = CreateDeterministicBytes(64 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var sessionId = await InterruptCopy(context, sourcePath, destinationPath, chunkSize: 16 * 1024, cancelAfterChunks: 2);

        var result = await context.Engine.ResumeAsync(
            sessionId,
            destinationPath,
            new CopyOptions { ChunkSize = 16 * 1024 },
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task ResumeAsync_WithCorruptedChunk_RecopiesAndCompletes()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        var sourceBytes = CreateDeterministicBytes(32 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var sessionId = await InterruptCopy(context, sourcePath, destinationPath, chunkSize: 8 * 1024, cancelAfterChunks: 2);

        var partPath = Path.Combine(context.StagingLocator.GetCacheDirectory(destinationPath), $"{sessionId}.part");
        using (var stream = new FileStream(partPath, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Seek(0, SeekOrigin.Begin);
            stream.WriteByte(0xFF);
        }

        var result = await context.Engine.ResumeAsync(
            sessionId,
            destinationPath,
            new CopyOptions { ChunkSize = 8 * 1024 },
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task ResumeAsync_WithChangedSource_Throws()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(16 * 1024));

        var sessionId = await InterruptCopy(context, sourcePath, destinationPath, chunkSize: 8 * 1024, cancelAfterChunks: 1);
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(20 * 1024));

        await Assert.ThrowsAsync<SourceChangedException>(() =>
            context.Engine.ResumeAsync(sessionId, destinationPath, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task ResumeAsync_DuringRecovery_CanBeCancelled()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(128 * 1024));

        var sessionId = await InterruptCopy(context, sourcePath, destinationPath, chunkSize: 16 * 1024, cancelAfterChunks: 1);

        using var cancellationSource = new CancellationTokenSource();
        var progress = new Progress<CopyProgress>(report =>
        {
            if (report.CompletedChunks >= 2)
            {
                cancellationSource.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Engine.ResumeAsync(
                sessionId,
                destinationPath,
                new CopyOptions { ChunkSize = 16 * 1024 },
                progress,
                cancellationSource.Token));

        Assert.False(File.Exists(destinationPath));
    }

    [Fact]
    public async Task ResumeAsync_AfterProcessRestartSimulation_CompletesCopy()
    {
        using var temp = new TempDirectory();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        var sourceBytes = CreateDeterministicBytes(48 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        string sessionId;
        var firstContext = new SqliteCopyEngineTestContext();
        sessionId = await InterruptCopy(firstContext, sourcePath, destinationPath, chunkSize: 12 * 1024, cancelAfterChunks: 2);

        var restartedContext = new SqliteCopyEngineTestContext();
        var result = await restartedContext.Engine.ResumeAsync(
            sessionId,
            destinationPath,
            new CopyOptions { ChunkSize = 12 * 1024 },
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    private static async Task<string> InterruptCopy(
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
