using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class StagingTests
{
    [Fact]
    public async Task CopyAsync_UsesDestinationCopyCacheDuringTransfer()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(128 * 1024));

        string? observedSessionId = null;
        var sawRunningWithoutFinalFile = false;

        var progress = new Progress<CopyProgress>(report =>
        {
            observedSessionId = report.SessionId;

            if (report.State == CopyState.Running)
            {
                var cacheDirectory = context.StagingLocator.GetCacheDirectory(destinationPath);
                var partPath = Path.Combine(cacheDirectory, $"{report.SessionId}.part");

                if (Directory.Exists(cacheDirectory) && File.Exists(partPath) && !File.Exists(destinationPath))
                {
                    sawRunningWithoutFinalFile = true;
                }
            }
        });

        var result = await context.Engine.CopyAsync(
            new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 16 * 1024 }),
            progress,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.NotNull(observedSessionId);
        Assert.True(sawRunningWithoutFinalFile);
        Assert.True(File.Exists(destinationPath));
        Assert.False(Directory.Exists(context.StagingLocator.GetCacheDirectory(destinationPath)));
    }

    [Fact]
    public void StagingLocator_PartFileLivesInsideDestinationCopyCache()
    {
        var locator = new StagingLocator();
        var destinationPath = @"C:\data\dest\file.bin";
        var session = new CopySession
        {
            SessionId = "abc123",
            SourcePath = @"D:\source\file.bin",
            DestinationPath = destinationPath,
            SourceIdentity = new SourceIdentity(10, DateTime.UtcNow, DateTime.UtcNow),
            StagingPath = string.Empty,
            ChunkSize = 1024,
            TotalChunks = 1
        };

        var partPath = locator.GetPartFilePath(session);

        Assert.Equal(@"C:\data\dest\.copycache\abc123.part", partPath);
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
