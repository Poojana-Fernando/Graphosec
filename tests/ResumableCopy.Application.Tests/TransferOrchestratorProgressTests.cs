using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Configuration;
using ResumableCopy.Application.Models;
using ResumableCopy.Application.Services;
using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Resume;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ResumableCopy.Application.Tests;

public class CopyProgressGuardTests
{
    [Theory]
    [InlineData(CopyState.Failed, CopyState.Running, false)]
    [InlineData(CopyState.WaitingForDestination, CopyState.Running, false)]
    [InlineData(CopyState.Paused, CopyState.Running, true)]
    [InlineData(CopyState.Cancelled, CopyState.Running, false)]
    [InlineData(CopyState.Running, CopyState.Paused, false)]
    [InlineData(CopyState.Running, CopyState.Running, true)]
    [InlineData(CopyState.Failed, CopyState.WaitingForDestination, true)]
    [InlineData(CopyState.Running, CopyState.Completed, true)]
    public void ShouldApplyProgress_RespectsTerminalAndWaitingStates(
        CopyState currentState,
        CopyState incomingState,
        bool expected)
    {
        Assert.Equal(expected, CopyProgressGuard.ShouldApplyProgress(currentState, incomingState));
    }
}

public class TransferOrchestratorProgressTests
{
    [Fact]
    public async Task StartCopyAsync_AfterDestinationError_KeepsWaitingStateDespiteStaleProgressFlush()
    {
        using var orchestrator = CreateOrchestrator(new StaleProgressThenFailCopyEngine());

        TransferSnapshot? latest = null;
        orchestrator.TransferChanged += (_, snapshot) => latest = snapshot;

        await orchestrator.StartCopyAsync(
            @"C:\source.bin",
            @"D:\dest.bin",
            new CopyOptions(),
            CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal(CopyState.WaitingForDestination, latest!.State);
        Assert.Equal("Waiting for device", latest.StatusText);
    }

    [Fact]
    public async Task StartCopyAsync_PassesOrchestratorSessionIdToCopyEngine()
    {
        var copyEngine = new SessionCapturingCopyEngine();
        using var orchestrator = CreateOrchestrator(copyEngine);

        var sessionId = await orchestrator.StartCopyAsync(
            @"C:\source.bin",
            @"D:\dest.bin",
            new CopyOptions(),
            CancellationToken.None);

        Assert.Equal(sessionId, copyEngine.LastSessionId);
    }

    [Fact]
    public async Task ResumeAsync_AppliesRunningProgressAfterPrepareForResume()
    {
        var copyEngine = new StalePausedThenRunningResumeEngine();
        using var orchestrator = CreateOrchestrator(copyEngine);

        var sessionId = await orchestrator.StartCopyAsync(
            @"C:\source.bin",
            @"D:\dest.bin",
            new CopyOptions(),
            CancellationToken.None);

        TransferSnapshot? latest = null;
        orchestrator.TransferChanged += (_, snapshot) => latest = snapshot;

        await orchestrator.ResumeAsync(sessionId, @"D:\dest.bin", new CopyOptions(), CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal(CopyState.Completed, latest!.State);
        Assert.True(latest.BytesCopied > 512 * 1024 * 1024);
        Assert.Equal("Completed", latest.StatusText);
    }

    private static TransferOrchestrator CreateOrchestrator(ICopyEngine copyEngine) =>
        new(
            copyEngine,
            new NoOpRecoveryService(),
            new NoOpSessionCleanupService(),
            new TestDeviceMonitor(),
            new TestDriveProvider(),
            new TestFileSystemService(),
            NullLogger<TransferOrchestrator>.Instance,
            Options.Create(new ResumableCopySettings()));

    private sealed class StaleProgressThenFailCopyEngine : ICopyEngine
    {
        public async Task<CopyResult> CopyAsync(
            CopyJob job,
            IProgress<CopyProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new CopyProgress(
                "session-1",
                CopyState.Running,
                64 * 1024 * 1024,
                4L * 1024 * 1024 * 1024,
                2,
                128));

            await Task.Yield();

            throw new DestinationUnavailableException("Destination volume is not ready.");
        }

        public Task<CopyResult> ResumeAsync(
            string sessionId,
            string destinationPath,
            CopyOptions? options,
            IProgress<CopyProgress>? progress,
            CancellationToken cancellationToken) =>
            CopyAsync(new CopyJob("source", destinationPath, options ?? new CopyOptions()), progress, cancellationToken);
    }

    private sealed class SessionCapturingCopyEngine : ICopyEngine
    {
        public string? LastSessionId { get; private set; }

        public Task<CopyResult> CopyAsync(
            CopyJob job,
            IProgress<CopyProgress>? progress,
            CancellationToken cancellationToken)
        {
            LastSessionId = job.SessionId;
            return Task.FromResult(new CopyResult(
                job.SessionId ?? "missing-session-id",
                job.SourcePath,
                job.DestinationPath,
                CopyState.Completed,
                0,
                TimeSpan.Zero));
        }

        public Task<CopyResult> ResumeAsync(
            string sessionId,
            string destinationPath,
            CopyOptions? options,
            IProgress<CopyProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CopyResult(
                sessionId,
                "source",
                destinationPath,
                CopyState.Completed,
                0,
                TimeSpan.Zero));
    }

    private sealed class StalePausedThenRunningResumeEngine : ICopyEngine
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
                128 * 1024 * 1024,
                TimeSpan.Zero));

        public async Task<CopyResult> ResumeAsync(
            string sessionId,
            string destinationPath,
            CopyOptions? options,
            IProgress<CopyProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Report(new CopyProgress(
                sessionId,
                CopyState.Paused,
                128 * 1024 * 1024,
                4L * 1024 * 1024 * 1024,
                1,
                128));

            await Task.Yield();

            progress?.Report(new CopyProgress(
                sessionId,
                CopyState.Running,
                1024L * 1024 * 1024,
                4L * 1024 * 1024 * 1024,
                64,
                128));

            return new CopyResult(
                sessionId,
                @"C:\source.bin",
                destinationPath,
                CopyState.Completed,
                4L * 1024 * 1024 * 1024,
                TimeSpan.Zero);
        }
    }
}

public class TransferOrchestratorReconnectTests
{
    [Fact]
    public async Task WaitingForDestination_WhenDeviceReturns_TransitionsToPaused()
    {
        var copyEngine = new DestinationUnavailableCopyEngine();
        var deviceMonitor = new TestDeviceMonitor();
        var fileSystem = new TestFileSystemService();
        fileSystem.AddFile(@"C:\source.bin");
        deviceMonitor.SetVolumeReady(@"D:\dest.bin", false);

        using var orchestrator = new TransferOrchestrator(
            copyEngine,
            new NoOpRecoveryService(),
            new NoOpSessionCleanupService(),
            deviceMonitor,
            new TestDriveProvider(),
            fileSystem,
            NullLogger<TransferOrchestrator>.Instance,
            Options.Create(new ResumableCopySettings
            {
                Diagnostics = new DiagnosticsSettings { ReconnectProbeIntervalMilliseconds = 50 }
            }));

        TransferSnapshot? latest = null;
        orchestrator.TransferChanged += (_, snapshot) => latest = snapshot;

        await orchestrator.StartCopyAsync(
            @"C:\source.bin",
            @"D:\dest.bin",
            new CopyOptions(),
            CancellationToken.None);

        Assert.Equal(CopyState.WaitingForDestination, latest!.State);

        await Task.Delay(100);
        Assert.Equal(CopyState.WaitingForDestination, latest.State);

        deviceMonitor.SetVolumeReady(@"D:\dest.bin", true);

        await WaitUntil(
            () => latest.State == CopyState.Paused,
            timeoutMilliseconds: 3000);

        Assert.Equal(CopyState.Paused, latest.State);
        Assert.Contains("Resume", latest.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(latest.CanResume);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMilliseconds)
            {
                throw new TimeoutException("Timed out waiting for reconnect probe.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class DestinationUnavailableCopyEngine : ICopyEngine
    {
        public Task<CopyResult> CopyAsync(CopyJob job, IProgress<CopyProgress>? progress, CancellationToken cancellationToken) =>
            throw new DestinationUnavailableException("Destination volume is not ready.");

        public Task<CopyResult> ResumeAsync(
            string sessionId,
            string destinationPath,
            CopyOptions? options,
            IProgress<CopyProgress>? progress,
            CancellationToken cancellationToken) =>
            CopyAsync(new CopyJob("source", destinationPath, options ?? new CopyOptions()), progress, cancellationToken);
    }
}

public class TransferOrchestratorCleanupTests
{
    [Fact]
    public async Task CancelAsync_WhenTransferIsPaused_MarksCancelledAndCleansUp()
    {
        var copyEngine = new PausedCopyEngine();
        var cleanup = new NoOpSessionCleanupService();
        using var orchestrator = new TransferOrchestrator(
            copyEngine,
            new NoOpRecoveryService(),
            cleanup,
            new TestDeviceMonitor(),
            new TestDriveProvider(),
            new TestFileSystemService(),
            NullLogger<TransferOrchestrator>.Instance,
            Options.Create(new ResumableCopySettings()));

        TransferSnapshot? latest = null;
        orchestrator.TransferChanged += (_, snapshot) => latest = snapshot;

        var sessionId = await orchestrator.StartCopyAsync(
            @"C:\source.bin",
            @"D:\dest.bin",
            new CopyOptions(),
            CancellationToken.None);

        Assert.Equal(CopyState.Paused, latest!.State);

        await orchestrator.CancelTransferAsync(sessionId, CancellationToken.None);

        Assert.NotNull(latest);
        Assert.Equal(CopyState.Cancelled, latest!.State);
        Assert.Equal("Cancelled", latest.StatusText);
        Assert.Contains(sessionId, cleanup.CleanedSessionIds);
    }

    [Fact]
    public async Task StartCopyAsync_WhenCancelled_InvokesSessionCleanup()
    {
        var copyEngine = new WaitingCopyEngine();
        var cleanup = new NoOpSessionCleanupService();
        string? sessionId = null;

        using var orchestrator = new TransferOrchestrator(
            copyEngine,
            new NoOpRecoveryService(),
            cleanup,
            new TestDeviceMonitor(),
            new TestDriveProvider(),
            new TestFileSystemService(),
            NullLogger<TransferOrchestrator>.Instance,
            Options.Create(new ResumableCopySettings()));

        orchestrator.TransferChanged += (_, snapshot) => sessionId ??= snapshot.SessionId;

        var copyTask = orchestrator.StartCopyAsync(
            @"C:\source.bin",
            @"D:\dest.bin",
            new CopyOptions(),
            CancellationToken.None);

        await WaitUntil(() => sessionId is not null);
        await orchestrator.CancelTransferAsync(sessionId!, CancellationToken.None);
        await copyTask;

        Assert.Contains(sessionId!, cleanup.CleanedSessionIds);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds = 2000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMilliseconds)
            {
                throw new TimeoutException("Timed out waiting for condition.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class PausedCopyEngine : ICopyEngine
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
                712 * 1024 * 1024,
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
                1024,
                TimeSpan.Zero));
    }

    private sealed class WaitingCopyEngine : ICopyEngine
    {
        public async Task<CopyResult> CopyAsync(CopyJob job, IProgress<CopyProgress>? progress, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Copy did not cancel.");
        }

        public Task<CopyResult> ResumeAsync(
            string sessionId,
            string destinationPath,
            CopyOptions? options,
            IProgress<CopyProgress>? progress,
            CancellationToken cancellationToken) =>
            CopyAsync(new CopyJob("source", destinationPath, options ?? new CopyOptions()), progress, cancellationToken);
    }
}
