using ResumableCopy.Core.Core;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Tests.TestSupport;
using Microsoft.Data.Sqlite;

namespace ResumableCopy.Core.Tests;

public class LazyChunkPersistenceTests
{
    [Fact]
    public async Task SaveAsync_DoesNotPersistChunkRowsUntilCompletion()
    {
        using var temp = new TempDirectory();
        var cacheDirectory = temp.GetPath(".copycache");
        using var repository = new SqliteSessionRepository(cacheDirectory);

        var session = CreateSession(temp, chunkCount: 5);
        await repository.SaveAsync(session, CancellationToken.None);

        Assert.Equal(0, CountChunkRows(cacheDirectory));

        var loaded = await repository.FindAsync(session.SessionId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(5, loaded!.Chunks.Count);
    }

    [Fact]
    public async Task MarkChunkCompleteAsync_InsertsChunkRowLazily()
    {
        using var temp = new TempDirectory();
        var cacheDirectory = temp.GetPath(".copycache");
        using var repository = new SqliteSessionRepository(cacheDirectory);

        var session = CreateSession(temp, chunkCount: 3);
        await repository.SaveAsync(session, CancellationToken.None);

        var chunk = session.Chunks[0];
        chunk.Hash = Convert.FromHexString("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF");
        chunk.IsComplete = true;
        await repository.MarkChunkCompleteAsync(session.SessionId, chunk, CancellationToken.None);

        Assert.Equal(1, CountChunkRows(cacheDirectory));
    }

    private static int CountChunkRows(string cacheDirectory)
    {
        var databasePath = Path.Combine(cacheDirectory, StagingLocator.DatabaseFileName);
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM chunks;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static CopySession CreateSession(TempDirectory temp, int chunkCount)
    {
        var identity = new SourceIdentity(chunkCount * 512L, DateTime.UtcNow, DateTime.UtcNow);
        var chunks = ChunkPlanner.CreateChunks(identity.Length, 512).ToList();

        var session = new CopySession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            SourcePath = temp.GetPath("source.bin"),
            DestinationPath = temp.GetPath("dest", "output.bin"),
            SourceIdentity = identity,
            StagingPath = temp.GetPath("dest", "output.part"),
            ChunkSize = 512,
            TotalChunks = chunkCount,
            Chunks = chunks,
            State = CopyState.Pending
        };

        return session;
    }
}
