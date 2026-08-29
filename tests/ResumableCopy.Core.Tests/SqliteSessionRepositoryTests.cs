using ResumableCopy.Core.Core;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Integrity;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Resume;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Storage.Sqlite;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class SqliteSessionRepositoryTests
{
    [Fact]
    public async Task SaveAsync_CreatesSession()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp);

        var session = CreateSampleSession(temp, chunkCount: 2);
        await repository.SaveAsync(session, CancellationToken.None);

        var loaded = await repository.FindAsync(session.SessionId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(session.SourcePath, loaded.SourcePath);
        Assert.Equal(session.DestinationPath, loaded.DestinationPath);
    }

    [Fact]
    public async Task FindAsync_ReturnsNullForMissingSession()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp);

        var loaded = await repository.FindAsync("missing", CancellationToken.None);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveAsync_UpdatesSessionState()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp);
        var session = CreateSampleSession(temp, chunkCount: 1);
        await repository.SaveAsync(session, CancellationToken.None);

        session.State = CopyState.Running;
        session.LastError = "temporary";
        await repository.SaveAsync(session, CancellationToken.None);

        var loaded = await repository.FindAsync(session.SessionId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(CopyState.Running, loaded.State);
        Assert.Equal("temporary", loaded.LastError);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSession()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp);
        var session = CreateSampleSession(temp, chunkCount: 1);
        await repository.SaveAsync(session, CancellationToken.None);

        await repository.DeleteAsync(session.SessionId, CancellationToken.None);

        var loaded = await repository.FindAsync(session.SessionId, CancellationToken.None);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveAsync_PersistsSourceIdentity()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp);
        var identity = new SourceIdentity(12345, new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var session = CreateSampleSession(temp, chunkCount: 0, identity: identity);

        await repository.SaveAsync(session, CancellationToken.None);

        var loaded = await repository.FindAsync(session.SessionId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.True(loaded.SourceIdentity.Matches(identity));
    }

    [Fact]
    public async Task SaveAsync_PersistsChunkMetadata()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp);
        var session = CreateSampleSession(temp, chunkCount: 3);
        await repository.SaveAsync(session, CancellationToken.None);

        var loaded = await repository.FindAsync(session.SessionId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(session.TotalChunks, loaded.Chunks.Count);
        Assert.Equal(0, loaded.Chunks[0].Offset);
        Assert.False(loaded.Chunks[0].IsComplete);
    }

    [Fact]
    public async Task MarkChunkCompleteAsync_PersistsVerifiedChunk()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp);
        var session = CreateSampleSession(temp, chunkCount: 2);
        await repository.SaveAsync(session, CancellationToken.None);

        var hash = Convert.FromHexString("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF");
        var chunk = session.Chunks[0];
        chunk.Hash = hash;
        chunk.IsComplete = true;

        await repository.MarkChunkCompleteAsync(session.SessionId, chunk, CancellationToken.None);

        var loaded = await repository.FindAsync(session.SessionId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.True(loaded.Chunks[0].IsComplete);
        Assert.Equal(hash, loaded.Chunks[0].Hash);
        Assert.False(loaded.Chunks[1].IsComplete);
    }

    [Fact]
    public async Task FindUnfinishedAsync_ReturnsOnlyUnfinishedSessions()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp);

        var unfinished = CreateSampleSession(temp, "unfinished", chunkCount: 1);
        unfinished.State = CopyState.Paused;
        var completed = CreateSampleSession(temp, "completed", chunkCount: 1);
        completed.State = CopyState.Completed;

        await repository.SaveAsync(unfinished, CancellationToken.None);
        await repository.SaveAsync(completed, CancellationToken.None);

        var results = await repository.FindUnfinishedAsync(CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(unfinished.SessionId, results[0].SessionId);
    }

    [Fact]
    public async Task RepositoryRecreation_PreservesPersistedData()
    {
        using var temp = new TempDirectory();
        var cacheDirectory = temp.GetPath("dest", ".copycache");
        var session = CreateSampleSession(temp, chunkCount: 2, cacheDirectoryOverride: cacheDirectory);

        using (var repository = new SqliteSessionRepository(cacheDirectory))
        {
            await repository.SaveAsync(session, CancellationToken.None);
            var chunk = session.Chunks[0];
            chunk.Hash = Convert.FromHexString("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF");
            chunk.IsComplete = true;
            await repository.MarkChunkCompleteAsync(session.SessionId, chunk, CancellationToken.None);
        }

        using var reopened = new SqliteSessionRepository(cacheDirectory);
        var loaded = await reopened.FindAsync(session.SessionId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.True(loaded.Chunks[0].IsComplete);
        Assert.Equal(CopyState.Pending, loaded.State);
    }

    [Fact]
    public async Task Initialization_IsIdempotent()
    {
        using var temp = new TempDirectory();
        var cacheDirectory = temp.GetPath(".copycache");

        using (var first = new SqliteSessionRepository(cacheDirectory))
        {
            var session = CreateSampleSession(temp, chunkCount: 1, cacheDirectoryOverride: cacheDirectory);
            await first.SaveAsync(session, CancellationToken.None);

            using var second = new SqliteSessionRepository(cacheDirectory);
            var loaded = await second.FindAsync(session.SessionId, CancellationToken.None);

            Assert.NotNull(loaded);
        }
    }

    [Fact]
    public void Database_IsLocatedInsideDestinationCopyCache()
    {
        using var temp = new TempDirectory();
        var cacheDirectory = temp.GetPath("dest", ".copycache");
        var stagingLocator = new StagingLocator();
        using var repository = new SqliteSessionRepository(cacheDirectory);

        Assert.Equal(stagingLocator.GetDatabasePath(temp.GetPath("dest", "output.bin")), repository.DatabasePath);
        Assert.True(File.Exists(repository.DatabasePath));
    }

    [Fact]
    public async Task MarkChunkCompleteAsync_DuplicateChunkIndex_UpdatesExistingRow()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp);
        var session = CreateSampleSession(temp, chunkCount: 1);
        await repository.SaveAsync(session, CancellationToken.None);

        var firstHash = Convert.FromHexString("1111111111111111111111111111111111111111111111111111111111111111");
        var secondHash = Convert.FromHexString("2222222222222222222222222222222222222222222222222222222222222222");

        var chunk = session.Chunks[0];
        chunk.Hash = firstHash;
        await repository.MarkChunkCompleteAsync(session.SessionId, chunk, CancellationToken.None);

        chunk.Hash = secondHash;
        await repository.MarkChunkCompleteAsync(session.SessionId, chunk, CancellationToken.None);

        var loaded = await repository.FindAsync(session.SessionId, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(secondHash, loaded.Chunks[0].Hash);
        Assert.Single(loaded.Chunks);
    }

    [Fact]
    public async Task MarkChunkCompleteAsync_WhenCommitFails_DoesNotPersistVerifiedChunk()
    {
        using var temp = new TempDirectory();
        var cacheDirectory = temp.GetPath(".copycache");
        var options = new SqliteSessionRepositoryOptions
        {
            BeforeCommit = _ => throw new InvalidOperationException("Injected commit failure.")
        };

        var session = CreateSampleSession(temp, chunkCount: 1, cacheDirectoryOverride: cacheDirectory);

        using (var repository = new SqliteSessionRepository(cacheDirectory, options))
        {
            await repository.SaveAsync(session, CancellationToken.None);

            var chunk = session.Chunks[0];
            chunk.Hash = Convert.FromHexString("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.MarkChunkCompleteAsync(session.SessionId, chunk, CancellationToken.None).AsTask());
        }

        using var reopened = new SqliteSessionRepository(cacheDirectory);
        var loaded = await reopened.FindAsync(session.SessionId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.False(loaded.Chunks[0].IsComplete);
        Assert.Null(loaded.Chunks[0].Hash);
    }

    [Fact]
    public async Task Dispose_DoesNotDeletePersistedData()
    {
        using var temp = new TempDirectory();
        var cacheDirectory = temp.GetPath(".copycache");
        var session = CreateSampleSession(temp, chunkCount: 1, cacheDirectoryOverride: cacheDirectory);

        var repository = new SqliteSessionRepository(cacheDirectory);
        await repository.SaveAsync(session, CancellationToken.None);
        repository.Dispose();

        using var reopened = new SqliteSessionRepository(cacheDirectory);
        var loaded = await reopened.FindAsync(session.SessionId, CancellationToken.None);
        Assert.NotNull(loaded);
    }

    [Fact]
    public void InvalidDatabase_ThrowsSessionPersistenceException()
    {
        using var temp = new TempDirectory();
        var cacheDirectory = temp.GetPath(".copycache");
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(Path.Combine(cacheDirectory, StagingLocator.DatabaseFileName), "not-a-sqlite-database");

        var exception = Assert.Throws<SessionPersistenceException>(() => new SqliteSessionRepository(cacheDirectory));
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task MultipleSessions_ArePersistedIndependently()
    {
        using var temp = new TempDirectory();
        using var repository = CreateRepository(temp);

        var first = CreateSampleSession(temp, "session-a", chunkCount: 1);
        var second = CreateSampleSession(temp, "session-b", chunkCount: 2);

        await repository.SaveAsync(first, CancellationToken.None);
        await repository.SaveAsync(second, CancellationToken.None);

        var loadedFirst = await repository.FindAsync(first.SessionId, CancellationToken.None);
        var loadedSecond = await repository.FindAsync(second.SessionId, CancellationToken.None);

        Assert.NotNull(loadedFirst);
        Assert.NotNull(loadedSecond);
        Assert.Single(loadedFirst.Chunks);
        Assert.Equal(2, loadedSecond.Chunks.Count);
    }

    [Fact]
    public async Task CopyEngine_WithSqliteProvider_PersistsSessionDuringCopy()
    {
        using var temp = new TempDirectory();
        var stagingLocator = new StagingLocator();
        var destinationPath = temp.GetPath("dest", "output.bin");
        var sourcePath = temp.GetPath("source.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(64 * 1024));

        var fileSystemService = new FileSystemService();
        var sessionRepositoryProvider = new SqliteSessionRepositoryProvider(stagingLocator);
        var engine = CopyEngineTestFactory.Create(fileSystemService, sessionRepositoryProvider);

        string? sessionId = null;
        var progress = new Progress<CopyProgress>(report => sessionId ??= report.SessionId);

        await engine.CopyAsync(
            new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 16 * 1024 }),
            progress,
            CancellationToken.None);

        var databasePath = stagingLocator.GetDatabasePath(destinationPath);
        Assert.False(File.Exists(databasePath));
        Assert.True(File.Exists(destinationPath));
        Assert.NotNull(sessionId);
    }

    private static SqliteSessionRepository CreateRepository(TempDirectory temp)
    {
        var cacheDirectory = temp.GetPath(".copycache");
        return new SqliteSessionRepository(cacheDirectory);
    }

    private static CopySession CreateSampleSession(
        TempDirectory temp,
        string sessionId = "session-1",
        int chunkCount = 1,
        SourceIdentity? identity = null,
        string? cacheDirectoryOverride = null)
    {
        var destinationPath = temp.GetPath("dest", "output.bin");
        var stagingLocator = new StagingLocator();
        const int chunkSize = 512;
        identity ??= new SourceIdentity(chunkCount * (long)chunkSize, DateTime.UtcNow, DateTime.UtcNow);
        var chunks = ChunkPlanner.CreateChunks(identity.Length, chunkSize).ToList();

        var session = new CopySession
        {
            SessionId = sessionId,
            SourcePath = temp.GetPath("source.bin"),
            DestinationPath = destinationPath,
            SourceIdentity = identity,
            StagingPath = string.Empty,
            ChunkSize = chunkSize,
            TotalChunks = chunks.Count,
            Chunks = chunks,
            State = CopyState.Pending
        };

        session.StagingPath = stagingLocator.GetPartFilePath(session);

        if (cacheDirectoryOverride is not null)
        {
            Directory.CreateDirectory(cacheDirectoryOverride);
        }

        return session;
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
