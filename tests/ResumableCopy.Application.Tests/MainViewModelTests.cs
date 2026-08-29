using Microsoft.Extensions.Options;
using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Configuration;
using ResumableCopy.Application.Models;
using ResumableCopy.Application.Services;
using ResumableCopy.Application.ViewModels;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;

namespace ResumableCopy.Application.Tests;

public class UserMessageFormatterTests
{
    [Fact]
    public void GetStatusText_MapsWaitingForDestination()
    {
        Assert.Equal("Waiting for device", UserMessageFormatter.GetStatusText(CopyState.WaitingForDestination));
    }

    [Fact]
    public void GetUserMessage_ForDisconnectedDestination_IsActionable()
    {
        var message = UserMessageFormatter.GetUserMessage(CopyState.WaitingForDestination, "Device removed");

        Assert.Contains("Connect the destination storage device", message);
        Assert.Contains("Device removed", message);
    }
}

public class MainViewModelTests
{
    [Fact]
    public void StartTransferCommand_IsDisabledWithoutPaths()
    {
        using var viewModel = CreateViewModel(new FakeTransferOrchestrator());

        Assert.False(viewModel.StartTransferCommand.CanExecute(null));
    }

    [Fact]
    public void StartTransferCommand_IsEnabledWhenPathsAreSet()
    {
        using var viewModel = CreateViewModel(new FakeTransferOrchestrator());
        viewModel.SourcePath = @"C:\source.bin";
        viewModel.DestinationPath = @"D:\dest.bin";

        Assert.True(viewModel.StartTransferCommand.CanExecute(null));
    }

    [Fact]
    public void ApplySnapshot_AddsTransferToCollection()
    {
        var orchestrator = new FakeTransferOrchestrator();
        using var viewModel = CreateViewModel(orchestrator);
        viewModel.SourcePath = @"C:\source.bin";
        viewModel.DestinationPath = @"D:\dest.bin";

        orchestrator.RaiseChanged(CreateSnapshot("abc123", bytesCopied: 1024, totalBytes: 4096));

        Assert.Single(viewModel.Transfers);
        Assert.Equal("abc123", viewModel.Transfers[0].SessionId);
        Assert.Equal(25d, viewModel.Transfers[0].ProgressPercent);
    }

    [Fact]
    public void ApplySnapshot_CoalescesRapidUpdatesToLatestPerSession()
    {
        var orchestrator = new FakeTransferOrchestrator();
        var uiThread = new DeferredUiThread();
        using var viewModel = CreateViewModel(orchestrator, uiThread);

        orchestrator.RaiseChanged(CreateSnapshot("s1", bytesCopied: 100, totalBytes: 1000));
        orchestrator.RaiseChanged(CreateSnapshot("s1", bytesCopied: 400, totalBytes: 1000));
        orchestrator.RaiseChanged(CreateSnapshot("s1", bytesCopied: 900, totalBytes: 1000));

        Assert.Empty(viewModel.Transfers);

        uiThread.Drain();

        Assert.Single(viewModel.Transfers);
        Assert.Equal(90d, viewModel.Transfers[0].ProgressPercent);
        Assert.Equal(1, uiThread.PostedCount);
    }

    [Fact]
    public async Task StartTransferAsync_UpdatesStatusMessage()
    {
        var orchestrator = new FakeTransferOrchestrator();
        var uiThread = new ImmediateUiThread();
        using var viewModel = CreateViewModel(orchestrator, uiThread);
        viewModel.SourcePath = @"C:\source.bin";
        viewModel.DestinationPath = @"D:\dest.bin";

        await viewModel.StartTransferCommand.ExecuteAsync(null);

        Assert.Equal("Transfer operation finished.", viewModel.StatusMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task StartTransferAsync_WhenBackgroundFails_LeavesUiReadyWithError()
    {
        var orchestrator = new FakeTransferOrchestrator
        {
            StartException = new DestinationUnavailableException("Destination volume is not ready.")
        };
        using var viewModel = CreateViewModel(orchestrator);
        viewModel.SourcePath = @"C:\source.bin";
        viewModel.DestinationPath = @"Z:\dest.bin";

        await viewModel.StartTransferCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.Contains("not ready", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelSelectedTransfer_CancelsPausedTransferAndUpdatesStatus()
    {
        var orchestrator = new FakeTransferOrchestrator();
        var prompts = new FakeUserPromptService { ConfirmResult = true };
        using var viewModel = CreateViewModel(orchestrator, userPromptService: prompts);

        orchestrator.RaiseChanged(CreateSnapshot(
            "abc123",
            bytesCopied: 712 * 1024 * 1024,
            totalBytes: 2L * 1024 * 1024 * 1024,
            canCancel: true,
            state: CopyState.Paused));
        viewModel.SelectedTransfer = viewModel.Transfers[0];

        await viewModel.CancelSelectedTransferCommand.ExecuteAsync(null);

        Assert.Equal(1, prompts.ConfirmCallCount);
        Assert.Equal("abc123", orchestrator.CancelledSessionId);
        Assert.Equal("Transfer cancelled.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task CancelSelectedTransfer_WhenUserDeclines_DoesNotCancelTransfer()
    {
        var orchestrator = new FakeTransferOrchestrator();
        var prompts = new FakeUserPromptService { ConfirmResult = false };
        using var viewModel = CreateViewModel(orchestrator, userPromptService: prompts);

        orchestrator.RaiseChanged(CreateSnapshot(
            "abc123",
            bytesCopied: 712 * 1024 * 1024,
            totalBytes: 2L * 1024 * 1024 * 1024,
            canCancel: true,
            state: CopyState.Paused));
        viewModel.SelectedTransfer = viewModel.Transfers[0];

        await viewModel.CancelSelectedTransferCommand.ExecuteAsync(null);

        Assert.Equal(1, prompts.ConfirmCallCount);
        Assert.Null(orchestrator.CancelledSessionId);
    }

    [Fact]
    public void TransferStateFilters_PopulateCompletedPausedAndCancelledTabs()
    {
        var fake = new FakeTransferOrchestrator();
        using var viewModel = CreateViewModel(fake);

        fake.RaiseChanged(CreateSnapshot("completed-1", 100, 100, state: CopyState.Completed));
        fake.RaiseChanged(CreateSnapshot("paused-1", 50, 100, canPause: true, state: CopyState.Paused));
        fake.RaiseChanged(CreateSnapshot("cancelled-1", 10, 100, state: CopyState.Cancelled));
        fake.RaiseChanged(CreateSnapshot("running-1", 25, 100, canPause: true, state: CopyState.Running));

        Assert.Equal(4, viewModel.Transfers.Count);
        Assert.Single(viewModel.CompletedTransfers);
        Assert.Single(viewModel.PausedTransfers);
        Assert.Single(viewModel.CancelledTransfers);
        Assert.Equal("completed-1", viewModel.CompletedTransfers[0].SessionId);
        Assert.Equal("paused-1", viewModel.PausedTransfers[0].SessionId);
        Assert.Equal("cancelled-1", viewModel.CancelledTransfers[0].SessionId);
    }

    [Fact]
    public async Task PauseSelectedTransfer_IsEnabledWhileStartTransferIsBusy()
    {
        var orchestrator = new BlockingTransferOrchestrator();
        using var viewModel = CreateViewModel(orchestrator);
        viewModel.SourcePath = @"C:\source.bin";
        viewModel.DestinationPath = @"D:\dest.bin";

        var startTask = viewModel.StartTransferCommand.ExecuteAsync(null);
        await WaitUntil(() => viewModel.IsBusy);

        orchestrator.RaiseChanged(CreateSnapshot(
            "abc123",
            bytesCopied: 40 * 1024 * 1024,
            totalBytes: 4L * 1024 * 1024 * 1024,
            canPause: true,
            canCancel: true));
        viewModel.SelectedTransfer = viewModel.Transfers[0];

        Assert.True(viewModel.PauseSelectedTransferCommand.CanExecute(null));

        viewModel.PauseSelectedTransferCommand.Execute(null);
        Assert.Equal("abc123", orchestrator.PausedSessionId);

        orchestrator.UnblockStart();
        await startTask;
    }

    [Fact]
    public async Task RapidSourceSelection_KeepsLatestResultAndDropsStaleAnalysis()
    {
        var analysis = new ControllablePathAnalysisService();
        using var viewModel = CreateViewModel(new FakeTransferOrchestrator(), pathAnalysis: analysis);

        viewModel.SourcePath = @"C:\old.bin";
        viewModel.SourcePath = @"C:\new.bin";

        analysis.CompleteSource(@"C:\old.bin", new PathAnalysis(@"C:\old.bin", true, true, 111, null, null));
        analysis.CompleteSource(@"C:\new.bin", new PathAnalysis(@"C:\new.bin", true, true, 999_999, null, null));

        await WaitUntil(() => viewModel.SourceInfoText.Contains("KB", StringComparison.OrdinalIgnoreCase)
            || viewModel.SourceInfoText.Contains("999", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain("111", viewModel.SourceInfoText);
        Assert.Contains("976.56 KB", viewModel.SourceInfoText);
    }

    [Fact]
    public async Task RapidDestinationSelection_KeepsLatestResult()
    {
        var analysis = new ControllablePathAnalysisService();
        using var viewModel = CreateViewModel(new FakeTransferOrchestrator(), pathAnalysis: analysis);

        viewModel.DestinationPath = @"D:\old.bin";
        viewModel.DestinationPath = @"E:\new.bin";

        analysis.CompleteDestination(@"D:\old.bin", new PathAnalysis(@"D:\old.bin", false, true, 0, 10, null));
        analysis.CompleteDestination(@"E:\new.bin", new PathAnalysis(@"E:\new.bin", false, true, 0, 5L * 1024 * 1024 * 1024, null));

        await WaitUntil(() => viewModel.DestinationInfoText.Contains("GB", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain("10 B", viewModel.DestinationInfoText);
        Assert.Contains("5 GB", viewModel.DestinationInfoText);
    }

    [Fact]
    public async Task SourceAnalysisFailure_DoesNotLeaveBusyState()
    {
        var analysis = new ControllablePathAnalysisService();
        using var viewModel = CreateViewModel(new FakeTransferOrchestrator(), pathAnalysis: analysis);

        viewModel.SourcePath = @"Z:\missing.bin";
        analysis.FailSource(@"Z:\missing.bin", new IOException("device not ready"));

        await WaitUntil(() => viewModel.SourceInfoText.Contains("Unable to analyze", StringComparison.OrdinalIgnoreCase));

        Assert.False(viewModel.IsBusy);
        Assert.Equal("Ready.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RecoverSessionsAsync_PassesFullDestinationFilePath()
    {
        var orchestrator = new FakeTransferOrchestrator();
        using var viewModel = CreateViewModel(orchestrator);
        viewModel.DestinationPath = @"D:\Lightyear-Frontier-AnkerGames.zip";

        await viewModel.RecoverSessionsCommand.ExecuteAsync(null);

        Assert.Equal(@"D:\Lightyear-Frontier-AnkerGames.zip", orchestrator.LastRecoveryDestinationPath);
    }

    private static MainViewModel CreateViewModel(
        ITransferOrchestrator orchestrator,
        IUiThread? uiThread = null,
        IPathAnalysisService? pathAnalysis = null,
        IUserPromptService? userPromptService = null) =>
        new(
            orchestrator,
            new FakeFilePickerService(),
            uiThread ?? new ImmediateUiThread(),
            new InlineBackgroundExecutor(),
            pathAnalysis ?? new ImmediatePathAnalysisService(),
            Options.Create(new ResumableCopySettings()),
            userPromptService: userPromptService);

    private static TransferSnapshot CreateSnapshot(
        string sessionId,
        long bytesCopied,
        long totalBytes,
        bool canCancel = false,
        bool canPause = false,
        CopyState state = CopyState.Running) =>
        new()
        {
            SessionId = sessionId,
            SourcePath = @"C:\source.bin",
            DestinationPath = @"D:\dest.bin",
            State = state,
            BytesCopied = bytesCopied,
            TotalBytes = totalBytes,
            CompletedChunks = 1,
            TotalChunks = 4,
            StatusText = UserMessageFormatter.GetStatusText(state),
            CanCancel = canCancel,
            CanPause = canPause
        };

    private static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds = 2000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMilliseconds)
            {
                throw new TimeoutException("Timed out waiting for condition.");
            }

            await Task.Yield();
        }
    }

    private class FakeTransferOrchestrator : ITransferOrchestrator
    {
        public event EventHandler<TransferSnapshot>? TransferChanged;

        public event EventHandler<string>? TransferRemoved;

        public Exception? StartException { get; init; }

        public string? CancelledSessionId { get; private set; }

        public string? LastRecoveryDestinationPath { get; private set; }

        public string? PausedSessionId { get; private set; }

        public void Dispose()
        {
        }

        public Task DiscoverRecoverableSessionsAsync(string destinationDirectory, CancellationToken cancellationToken = default)
        {
            LastRecoveryDestinationPath = destinationDirectory;
            return Task.CompletedTask;
        }

        public Task ClearFinishedTransfersAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public IReadOnlyList<TransferSnapshot> GetTransfers() => [];

        public TransferSnapshot? GetTransfer(string sessionId) => null;

        public void RequestPause(string sessionId) => PausedSessionId = sessionId;

        public Task CancelTransferAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            CancelledSessionId = sessionId;
            RaiseChanged(new TransferSnapshot
            {
                SessionId = sessionId,
                SourcePath = @"C:\source.bin",
                DestinationPath = @"D:\dest.bin",
                State = CopyState.Cancelled,
                BytesCopied = 712 * 1024 * 1024,
                TotalBytes = 2L * 1024 * 1024 * 1024,
                StatusText = "Cancelled",
            });
            return Task.CompletedTask;
        }

        public Task RemoveTransferAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            TransferRemoved?.Invoke(this, sessionId);
            return Task.CompletedTask;
        }

        public Task RecoverSessionAsync(string destinationPath, string sessionId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ResumeAsync(
            string sessionId,
            string destinationPath,
            CopyOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> StartCopyAsync(
            string sourcePath,
            string destinationPath,
            CopyOptions options,
            CancellationToken cancellationToken = default) =>
            StartCopyInternalAsync(sourcePath, destinationPath, options, cancellationToken);

        protected virtual Task<string> StartCopyInternalAsync(
            string sourcePath,
            string destinationPath,
            CopyOptions options,
            CancellationToken cancellationToken = default) =>
            StartException is null
                ? Task.FromResult("session-1")
                : Task.FromException<string>(StartException);

        public void NotifyVolumesChanged()
        {
        }

        public Task LoadPersistedHistoryAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void RaiseChanged(TransferSnapshot snapshot) =>
            TransferChanged?.Invoke(this, snapshot);
    }

    private sealed class BlockingTransferOrchestrator : FakeTransferOrchestrator
    {
        private readonly TaskCompletionSource _startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<string> StartCopyInternalAsync(
            string sourcePath,
            string destinationPath,
            CopyOptions options,
            CancellationToken cancellationToken = default)
        {
            if (StartException is not null)
            {
                return Task.FromException<string>(StartException);
            }

            return WaitAndReturnSessionAsync(cancellationToken);
        }

        public void UnblockStart() => _startGate.TrySetResult();

        private async Task<string> WaitAndReturnSessionAsync(CancellationToken cancellationToken)
        {
            await _startGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return "session-1";
        }
    }

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public string? PickDestinationFile(string? sourceFilePath) => null;

        public string? PickSourceFile() => null;
    }

    private sealed class FakeUserPromptService : IUserPromptService
    {
        public bool ConfirmResult { get; init; } = true;

        public int ConfirmCallCount { get; private set; }

        public bool Confirm(string title, string message)
        {
            ConfirmCallCount++;
            return ConfirmResult;
        }
    }

    private sealed class ImmediateUiThread : IUiThread
    {
        public void Invoke(Action action) => action();

        public void Post(Action action) => action();
    }

    private sealed class DeferredUiThread : IUiThread
    {
        private readonly Queue<Action> _posted = new();

        public int PostedCount { get; private set; }

        public void Invoke(Action action) => action();

        public void Post(Action action)
        {
            PostedCount++;
            _posted.Enqueue(action);
        }

        public void Drain()
        {
            while (_posted.Count > 0)
            {
                _posted.Dequeue()();
            }
        }
    }

    private sealed class InlineBackgroundExecutor : IBackgroundExecutor
    {
        public Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
            work(cancellationToken);

        public Task<TResult> RunAsync<TResult>(
            Func<CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken = default) =>
            work(cancellationToken);
    }

    private sealed class ImmediatePathAnalysisService : IPathAnalysisService
    {
        public Task<PathAnalysis> AnalyzeSourceAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(PathAnalysis.Empty(path));

        public Task<PathAnalysis> AnalyzeDestinationAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(PathAnalysis.Empty(path));
    }

    private sealed class ControllablePathAnalysisService : IPathAnalysisService
    {
        private readonly List<(string Path, TaskCompletionSource<PathAnalysis> Tcs)> _source = [];
        private readonly List<(string Path, TaskCompletionSource<PathAnalysis> Tcs)> _destination = [];

        public Task<PathAnalysis> AnalyzeSourceAsync(string path, CancellationToken cancellationToken = default) =>
            Enqueue(_source, path, cancellationToken);

        public Task<PathAnalysis> AnalyzeDestinationAsync(string path, CancellationToken cancellationToken = default) =>
            Enqueue(_destination, path, cancellationToken);

        public void CompleteSource(string path, PathAnalysis analysis) => Complete(_source, path, analysis);

        public void CompleteDestination(string path, PathAnalysis analysis) => Complete(_destination, path, analysis);

        public void FailSource(string path, Exception exception) => Fail(_source, path, exception);

        private static Task<PathAnalysis> Enqueue(
            List<(string Path, TaskCompletionSource<PathAnalysis> Tcs)> store,
            string path,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<PathAnalysis>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            lock (store)
            {
                store.Add((path, tcs));
            }

            return tcs.Task;
        }

        private static void Complete(
            List<(string Path, TaskCompletionSource<PathAnalysis> Tcs)> store,
            string path,
            PathAnalysis analysis)
        {
            TaskCompletionSource<PathAnalysis>? tcs = null;
            lock (store)
            {
                var index = store.FindIndex(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    tcs = store[index].Tcs;
                    store.RemoveAt(index);
                }
            }

            tcs?.TrySetResult(analysis);
        }

        private static void Fail(
            List<(string Path, TaskCompletionSource<PathAnalysis> Tcs)> store,
            string path,
            Exception exception)
        {
            TaskCompletionSource<PathAnalysis>? tcs = null;
            lock (store)
            {
                var index = store.FindIndex(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    tcs = store[index].Tcs;
                    store.RemoveAt(index);
                }
            }

            tcs?.TrySetException(exception);
        }
    }
}
