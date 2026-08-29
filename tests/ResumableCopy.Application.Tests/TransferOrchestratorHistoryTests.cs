using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Configuration;
using ResumableCopy.Application.Models;
using ResumableCopy.Application.Services;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Application.Tests;

public class TransferOrchestratorHistoryTests
{
    [Fact]
    public async Task LoadPersistedHistoryAsync_RestoresSavedTransfers()
    {
        var historyPath = CreateTempHistoryPath();
        var historyStore = new JsonTransferHistoryStore(historyPath);
        await historyStore.UpsertAsync(new TransferHistoryRecord
        {
            SessionId = "saved-session",
            SourcePath = @"C:\saved.bin",
            DestinationPath = @"D:\saved.bin",
            State = CopyState.Paused,
            BytesCopied = 2048,
            TotalBytes = 8192,
            CompletedChunks = 2,
            TotalChunks = 8,
            ErrorMessage = "Transfer paused."
        });

        using var orchestrator = CreateOrchestrator(historyStore);
        await orchestrator.LoadPersistedHistoryAsync();

        var transfers = orchestrator.GetTransfers();
        Assert.Single(transfers);
        Assert.Equal("saved-session", transfers[0].SessionId);
        Assert.Equal(CopyState.Paused, transfers[0].State);
        Assert.True(transfers[0].CanResume);
    }

    private static TransferOrchestrator CreateOrchestrator(ITransferHistoryStore historyStore) =>
        new(
            new NoOpCopyEngine(),
            new NoOpRecoveryService(),
            new NoOpSessionCleanupService(),
            new TestDeviceMonitor(),
            new TestDriveProvider(),
            new TestFileSystemService(),
            NullLogger<TransferOrchestrator>.Instance,
            Options.Create(new ResumableCopySettings()),
            historyStore);

    private static string CreateTempHistoryPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ResumableCopyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "history.json");
    }

    private sealed class NoOpCopyEngine : Core.Abstractions.ICopyEngine
    {
        public Task<Core.Domain.CopyResult> CopyAsync(
            Core.Domain.CopyJob job,
            IProgress<Core.Domain.CopyProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Core.Domain.CopyResult(
                "session",
                job.SourcePath,
                job.DestinationPath,
                CopyState.Completed,
                0,
                TimeSpan.Zero));

        public Task<Core.Domain.CopyResult> ResumeAsync(
            string sessionId,
            string destinationPath,
            Core.Domain.CopyOptions? options,
            IProgress<Core.Domain.CopyProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Core.Domain.CopyResult(
                sessionId,
                "source",
                destinationPath,
                CopyState.Completed,
                0,
                TimeSpan.Zero));
    }
}
