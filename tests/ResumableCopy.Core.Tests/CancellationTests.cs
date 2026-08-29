using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class CancellationTests
{
    [Fact]
    public async Task CopyAsync_WhenCancelled_LeavesPartFileAndUnfinishedSession()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(512 * 1024));

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
                new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 64 * 1024 }),
                progress,
                cancellationSource.Token));

        Assert.NotNull(sessionId);
        Assert.False(File.Exists(destinationPath));

        var cacheDirectory = context.StagingLocator.GetCacheDirectory(destinationPath);
        var partPath = Path.Combine(cacheDirectory, $"{sessionId}.part");
        Assert.True(File.Exists(partPath));

        var unfinished = await context.SessionRepository.FindUnfinishedAsync(CancellationToken.None);
        Assert.Contains(unfinished, session => session.SessionId == sessionId && session.State == CopyState.Paused);
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
