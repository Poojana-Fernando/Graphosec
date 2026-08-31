using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Configuration;
using ResumableCopy.Application.Models;
using ResumableCopy.Application.Services;
using ResumableCopy.Core.Abstractions;
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

    [Fact]
    public async Task LoadPersistedHistoryAsync_DiscoverOrphanedSessionsFromRegisteredDestination()
    {
        var destinationPath = @"D:\orphaned.bin";
        var registryPath = Path.Combine(Path.GetTempPath(), "ResumableCopyTests", Guid.NewGuid().ToString("N"), "destinations.json");
        Directory.CreateDirectory(Path.GetDirectoryName(registryPath)!);
        var destinationRegistry = new JsonDestinationRegistry(registryPath);
        await destinationRegistry.RegisterAsync(destinationPath);

        var recoveryService = new NoOpRecoveryService
        {
            DiscoverHandler = path =>
            {
                if (!string.Equals(path, destinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    return [];
                }

                return
                [
                    new RecoverableSessionInfo(
                        "orphaned-session",
                        @"C:\source.bin",
                        destinationPath,
                        CopyState.Paused,
                        4096,
                        1024,
                        1,
                        4,
                        "Transfer paused.")
                ];
            }
        };

        using var orchestrator = CreateOrchestrator(
            new NullTransferHistoryStore(),
            destinationRegistry,
            recoveryService);
        await orchestrator.LoadPersistedHistoryAsync();

        var transfers = orchestrator.GetTransfers();
        Assert.Single(transfers);
        Assert.Equal("orphaned-session", transfers[0].SessionId);
        Assert.Equal(CopyState.Paused, transfers[0].State);
        Assert.True(transfers[0].CanResume);
    }

    [Fact]
    public async Task LoadPersistedHistoryAsync_RefreshesStaleHistoryFromDestinationCache()
    {
        var destinationPath = @"D:\refresh.bin";
        var historyPath = CreateTempHistoryPath();
        var historyStore = new JsonTransferHistoryStore(historyPath);
        await historyStore.UpsertAsync(new TransferHistoryRecord
        {
            SessionId = "shared-session",
            SourcePath = @"C:\source.bin",
            DestinationPath = destinationPath,
            State = CopyState.Pending,
            BytesCopied = 0,
            TotalBytes = 0,
            CompletedChunks = 0,
            TotalChunks = 0
        });

        var recoveryService = new NoOpRecoveryService
        {
            DiscoverHandler = _ =>
            [
                new RecoverableSessionInfo(
                    "shared-session",
                    @"C:\source.bin",
                    destinationPath,
                    CopyState.Paused,
                    8192,
                    4096,
                    2,
                    8,
                    "Transfer paused.")
            ]
        };

        using var orchestrator = CreateOrchestrator(historyStore, recoveryService: recoveryService);
        await orchestrator.LoadPersistedHistoryAsync();

        var transfer = Assert.Single(orchestrator.GetTransfers());
        Assert.Equal(CopyState.Paused, transfer.State);
        Assert.Equal(4096, transfer.BytesCopied);
        Assert.Equal(8192, transfer.TotalBytes);
        Assert.True(transfer.CanResume);
    }

    [Fact]
    public async Task LoadPersistedHistoryAsync_DoesNotOverwriteCancelledHistoryWithOrphanedPausedSession()
    {
        var destinationPath = @"D:\cancelled.bin";
        var historyPath = CreateTempHistoryPath();
        var historyStore = new JsonTransferHistoryStore(historyPath);
        await historyStore.UpsertAsync(new TransferHistoryRecord
        {
            SessionId = "shared-session",
            SourcePath = @"C:\source.bin",
            DestinationPath = destinationPath,
            State = CopyState.Cancelled,
            BytesCopied = 4096,
            TotalBytes = 4096,
            CompletedChunks = 4,
            TotalChunks = 4,
            ErrorMessage = "Transfer cancelled."
        });

        var recoveryService = new NoOpRecoveryService
        {
            DiscoverHandler = _ =>
            [
                new RecoverableSessionInfo(
                    "shared-session",
                    @"C:\source.bin",
                    destinationPath,
                    CopyState.Paused,
                    4096,
                    2048,
                    2,
                    4,
                    "Transfer paused.")
            ]
        };

        using var orchestrator = CreateOrchestrator(historyStore, recoveryService: recoveryService);
        await orchestrator.LoadPersistedHistoryAsync();

        var transfer = Assert.Single(orchestrator.GetTransfers());
        Assert.Equal(CopyState.Cancelled, transfer.State);
        Assert.Equal("Cancelled", transfer.StatusText);
    }

    private static TransferOrchestrator CreateOrchestrator(
        ITransferHistoryStore historyStore,
        IDestinationRegistry? destinationRegistry = null,
        ITransferRecoveryService? recoveryService = null) =>
        new(
            new NoOpCopyEngine(),
            recoveryService ?? new NoOpRecoveryService(),
            new NoOpSessionCleanupService(),
            new TestDeviceMonitor(),
            new TestDriveProvider(),
            new TestFileSystemService(),
            NullLogger<TransferOrchestrator>.Instance,
            Options.Create(new ResumableCopySettings()),
            historyStore,
            destinationRegistry);

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
