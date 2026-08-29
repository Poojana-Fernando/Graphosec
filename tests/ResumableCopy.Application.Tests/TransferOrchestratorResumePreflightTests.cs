using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResumableCopy.Application.Configuration;
using ResumableCopy.Application.Services;
using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;

namespace ResumableCopy.Application.Tests;

public class TransferOrchestratorResumePreflightTests
{
    [Fact]
    public async Task ResumeAsync_WhenDestinationVolumeMissing_DoesNotSetRunning()
    {
        var copyEngine = new ResumeTrackingCopyEngine();
        var recovery = new NoOpRecoveryService
        {
            RecoverSessionHandler = (_, sessionId) =>
                NoOpRecoveryService.CreateUnavailableDestinationResult(sessionId, @"D:\dest.bin"),
        };

        using var orchestrator = CreateOrchestrator(copyEngine, recovery);

        CopyState? observedState = null;
        orchestrator.TransferChanged += (_, snapshot) => observedState = snapshot.State;

        var sessionId = await orchestrator.StartCopyAsync(
            @"C:\source.bin",
            @"D:\dest.bin",
            new CopyOptions(),
            CancellationToken.None);

        observedState = null;

        var exception = await Assert.ThrowsAsync<DestinationUnavailableException>(() =>
            orchestrator.ResumeAsync(sessionId, @"D:\dest.bin", new CopyOptions(), CancellationToken.None));

        Assert.Contains("Destination volume is not ready", exception.Message);
        Assert.False(copyEngine.ResumeWasCalled);
        Assert.NotEqual(CopyState.Running, observedState);

        var snapshot = orchestrator.GetTransfer(sessionId);
        Assert.NotNull(snapshot);
        Assert.Equal(CopyState.WaitingForDestination, snapshot!.State);
        Assert.Equal("Waiting for device", snapshot.StatusText);
    }

    [Fact]
    public async Task ResumeAsync_WhenDestinationVolumeMissing_PreservesProgress()
    {
        const long copiedBytes = 512L * 1024 * 1024;
        var copyEngine = new PausedAtBytesCopyEngine(copiedBytes);
        var recovery = new NoOpRecoveryService
        {
            RecoverSessionHandler = (_, sessionId) =>
                NoOpRecoveryService.CreateUnavailableDestinationResult(sessionId, @"D:\dest.bin"),
        };

        using var orchestrator = CreateOrchestrator(copyEngine, recovery);

        var sessionId = await orchestrator.StartCopyAsync(
            @"C:\source.bin",
            @"D:\dest.bin",
            new CopyOptions(),
            CancellationToken.None);

        await Assert.ThrowsAsync<DestinationUnavailableException>(() =>
            orchestrator.ResumeAsync(sessionId, @"D:\dest.bin", new CopyOptions(), CancellationToken.None));

        var snapshot = orchestrator.GetTransfer(sessionId);
        Assert.NotNull(snapshot);
        Assert.Equal(copiedBytes, snapshot!.BytesCopied);
        Assert.Equal(CopyState.WaitingForDestination, snapshot.State);
    }

    [Fact]
    public async Task ResumeAsync_WhenDiscoverThrowsDestinationUnavailable_PropagatesException()
    {
        var copyEngine = new ResumeTrackingCopyEngine();
        var recovery = new NoOpRecoveryService
        {
            RecoverSessionHandler = (_, sessionId) =>
                new RecoveryResult(
                    sessionId,
                    CopyState.Failed,
                    CanResume: false,
                    InvalidatedChunkCount: 0,
                    Message: $"Session '{sessionId}' was not found."),
            DiscoverExceptionFactory = _ =>
                new DestinationUnavailableException("Destination storage is not available. Reconnect the drive and try again."),
        };

        using var orchestrator = CreateOrchestrator(copyEngine, recovery);

        var sessionId = await orchestrator.StartCopyAsync(
            @"C:\source.bin",
            @"D:\dest.bin",
            new CopyOptions(),
            CancellationToken.None);

        await Assert.ThrowsAsync<DestinationUnavailableException>(() =>
            orchestrator.ResumeAsync(sessionId, @"D:\dest.bin", new CopyOptions(), CancellationToken.None));

        Assert.False(copyEngine.ResumeWasCalled);
    }

    private static TransferOrchestrator CreateOrchestrator(
        ICopyEngine copyEngine,
        NoOpRecoveryService recoveryService) =>
        new(
            copyEngine,
            recoveryService,
            new NoOpSessionCleanupService(),
            new TestDeviceMonitor(),
            new TestDriveProvider(),
            new TestFileSystemService(),
            NullLogger<TransferOrchestrator>.Instance,
            Options.Create(new ResumableCopySettings()));

    private sealed class ResumeTrackingCopyEngine : ICopyEngine
    {
        public bool ResumeWasCalled { get; private set; }

        public Task<CopyResult> CopyAsync(
            CopyJob job,
            IProgress<CopyProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CopyResult(
                job.SessionId ?? "session-1",
                job.SourcePath,
                job.DestinationPath,
                CopyState.Paused,
                128 * 1024 * 1024,
                TimeSpan.Zero));

        public Task<CopyResult> ResumeAsync(
            string sessionId,
            string destinationPath,
            CopyOptions? options,
            IProgress<CopyProgress>? progress,
            CancellationToken cancellationToken)
        {
            ResumeWasCalled = true;
            return Task.FromResult(new CopyResult(
                sessionId,
                @"C:\source.bin",
                destinationPath,
                CopyState.Completed,
                1024,
                TimeSpan.Zero));
        }
    }

    private sealed class PausedAtBytesCopyEngine(long copiedBytes) : ICopyEngine
    {
        public Task<CopyResult> CopyAsync(
            CopyJob job,
            IProgress<CopyProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CopyResult(
                job.SessionId ?? "session-1",
                job.SourcePath,
                job.DestinationPath,
                CopyState.Paused,
                copiedBytes,
                TimeSpan.Zero));

        public Task<CopyResult> ResumeAsync(
            string sessionId,
            string destinationPath,
            CopyOptions? options,
            IProgress<CopyProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CopyResult(
                sessionId,
                @"C:\source.bin",
                destinationPath,
                CopyState.Completed,
                copiedBytes,
                TimeSpan.Zero));
    }
}

public class RecoveryFailureMapperTests
{
    [Fact]
    public void CreateException_WhenDestinationUnavailable_ReturnsDestinationUnavailableException()
    {
        var recovery = NoOpRecoveryService.CreateUnavailableDestinationResult("session-1", @"D:\dest.bin");

        var exception = Core.Resume.RecoveryFailureMapper.CreateException(recovery);

        Assert.IsType<DestinationUnavailableException>(exception);
    }
}
