using ResumableCopy.Application.Models;
using ResumableCopy.Application.Services;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Application.Tests;

public class JsonTransferHistoryStoreTests
{
    [Fact]
    public async Task UpsertAsync_PersistsAndReloadsRecords()
    {
        var path = CreateTempHistoryPath();
        var store = new JsonTransferHistoryStore(path);
        var record = CreateRecord("session-1", CopyState.Completed);

        await store.UpsertAsync(record);
        var loaded = await store.LoadAsync();

        Assert.Single(loaded);
        Assert.Equal("session-1", loaded[0].SessionId);
        Assert.Equal(CopyState.Completed, loaded[0].State);
    }

    [Fact]
    public async Task RemoveAsync_DeletesMatchingRecord()
    {
        var path = CreateTempHistoryPath();
        var store = new JsonTransferHistoryStore(path);
        await store.UpsertAsync(CreateRecord("keep", CopyState.Paused));
        await store.UpsertAsync(CreateRecord("remove", CopyState.Failed));

        await store.RemoveAsync("remove");
        var loaded = await store.LoadAsync();

        Assert.Single(loaded);
        Assert.Equal("keep", loaded[0].SessionId);
    }

    [Fact]
    public async Task RemoveFinishedAsync_RemovesCompletedFailedAndCancelled()
    {
        var path = CreateTempHistoryPath();
        var store = new JsonTransferHistoryStore(path);
        await store.UpsertAsync(CreateRecord("completed", CopyState.Completed));
        await store.UpsertAsync(CreateRecord("failed", CopyState.Failed));
        await store.UpsertAsync(CreateRecord("paused", CopyState.Paused));

        await store.RemoveFinishedAsync();
        var loaded = await store.LoadAsync();

        Assert.Single(loaded);
        Assert.Equal("paused", loaded[0].SessionId);
    }

    private static TransferHistoryRecord CreateRecord(string sessionId, CopyState state) =>
        new()
        {
            SessionId = sessionId,
            SourcePath = @"C:\source.bin",
            DestinationPath = @"D:\dest.bin",
            State = state,
            BytesCopied = 1024,
            TotalBytes = 4096,
            CompletedChunks = 1,
            TotalChunks = 4,
            ErrorMessage = null,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

    private static string CreateTempHistoryPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ResumableCopyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "history.json");
    }
}
