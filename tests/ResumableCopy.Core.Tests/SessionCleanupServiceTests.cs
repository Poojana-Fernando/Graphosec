using ResumableCopy.Core.Domain;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class SessionCleanupServiceTests
{
    [Fact]
    public async Task CleanupSessionAsync_RemovesPartFileAndCacheDirectoryWhenEmpty()
    {
        using var temp = new TempDirectory();
        var stagingLocator = new StagingLocator();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var cacheDirectory = stagingLocator.GetCacheDirectory(destinationPath);
        Directory.CreateDirectory(cacheDirectory);
        var sessionId = "abc123";
        var partPath = Path.Combine(cacheDirectory, $"{sessionId}.part");
        await File.WriteAllTextAsync(partPath, "partial");

        using (var repository = new SqliteSessionRepository(cacheDirectory))
        {
            await repository.SaveAsync(new CopySession
            {
                SessionId = sessionId,
                SourcePath = temp.GetPath("source.bin"),
                DestinationPath = destinationPath,
                StagingPath = partPath,
                SourceIdentity = new SourceIdentity(10, DateTime.UtcNow, DateTime.UtcNow),
                ChunkSize = 1024,
                TotalChunks = 1,
                State = CopyState.Paused
            }, CancellationToken.None);
        }

        var service = new SessionCleanupService(
            new FileSystemService(),
            stagingLocator,
            new SqliteSessionRepositoryProvider(stagingLocator));

        await service.CleanupSessionAsync(destinationPath, sessionId, CancellationToken.None);

        Assert.False(File.Exists(partPath));
        Assert.False(Directory.Exists(cacheDirectory));
    }
}
