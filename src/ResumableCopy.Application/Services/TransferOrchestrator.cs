using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Options;

using ResumableCopy.Application.Abstractions;

using ResumableCopy.Application.Configuration;

using ResumableCopy.Application.Diagnostics;

using ResumableCopy.Application.Models;

using ResumableCopy.Core.Abstractions;

using ResumableCopy.Core.Domain;

using ResumableCopy.Core.Errors;

using ResumableCopy.Core.Resume;



namespace ResumableCopy.Application.Services;



public sealed class TransferOrchestrator : ITransferOrchestrator

{

    private static readonly TimeSpan DefaultReconnectProbeInterval = TimeSpan.FromSeconds(3);



    private readonly ICopyEngine _copyEngine;

    private readonly ITransferRecoveryService _recoveryService;

    private readonly ISessionCleanupService _sessionCleanupService;

    private readonly IDeviceMonitor _deviceMonitor;

    private readonly IDriveProvider _driveProvider;

    private readonly IFileSystemService _fileSystemService;

    private readonly ITransferHistoryStore _historyStore;

    private readonly IDestinationRegistry _destinationRegistry;

    private readonly ILogger<TransferOrchestrator> _logger;

    private readonly TimeSpan _progressInterval;

    private readonly TimeSpan _reconnectProbeInterval;

    private readonly ConcurrentDictionary<string, TransferRuntime> _transfers = new(StringComparer.OrdinalIgnoreCase);

    private readonly Timer _reconnectTimer;

    private int _disposed;



    public TransferOrchestrator(

        ICopyEngine copyEngine,

        ITransferRecoveryService recoveryService,

        ISessionCleanupService sessionCleanupService,

        IDeviceMonitor deviceMonitor,

        IDriveProvider driveProvider,

        IFileSystemService fileSystemService,

        ILogger<TransferOrchestrator> logger,

        IOptions<ResumableCopySettings>? settings = null,

        ITransferHistoryStore? historyStore = null,

        IDestinationRegistry? destinationRegistry = null)

    {

        _copyEngine = copyEngine ?? throw new ArgumentNullException(nameof(copyEngine));

        _recoveryService = recoveryService ?? throw new ArgumentNullException(nameof(recoveryService));

        _sessionCleanupService = sessionCleanupService ?? throw new ArgumentNullException(nameof(sessionCleanupService));

        _deviceMonitor = deviceMonitor ?? throw new ArgumentNullException(nameof(deviceMonitor));

        _driveProvider = driveProvider ?? throw new ArgumentNullException(nameof(driveProvider));

        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _historyStore = historyStore ?? new NullTransferHistoryStore();

        _destinationRegistry = destinationRegistry ?? new NullDestinationRegistry();

        var intervalMs = settings?.Value.Diagnostics.ProgressUpdateIntervalMilliseconds ?? 0;

        _progressInterval = TimeSpan.FromMilliseconds(intervalMs > 0 ? intervalMs : 200);

        var reconnectMs = settings?.Value.Diagnostics.ReconnectProbeIntervalMilliseconds ?? 0;

        _reconnectProbeInterval = TimeSpan.FromMilliseconds(reconnectMs > 0 ? reconnectMs : DefaultReconnectProbeInterval.TotalMilliseconds);

        _reconnectTimer = new Timer(CheckWaitingTransfers, null, _reconnectProbeInterval, _reconnectProbeInterval);

    }



    public event EventHandler<TransferSnapshot>? TransferChanged;



    public event EventHandler<string>? TransferRemoved;



    public IReadOnlyList<TransferSnapshot> GetTransfers()

    {

        return _transfers.Values

            .Select(static runtime => runtime.Snapshot)

            .OrderByDescending(static snapshot => snapshot.State == CopyState.Running)

            .ThenBy(static snapshot => snapshot.SourcePath, StringComparer.OrdinalIgnoreCase)

            .ToArray();

    }



    public TransferSnapshot? GetTransfer(string sessionId)

    {

        return _transfers.TryGetValue(sessionId, out var runtime) ? runtime.Snapshot : null;

    }



    public async Task<string> StartCopyAsync(

        string sourcePath,

        string destinationPath,

        CopyOptions options,

        CancellationToken cancellationToken = default)

    {

        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        ArgumentNullException.ThrowIfNull(options);



        var runtime = new TransferRuntime(sourcePath, destinationPath, options);

        if (!_transfers.TryAdd(runtime.SessionId, runtime))

        {

            throw new InvalidOperationException("Failed to register a new transfer.");

        }



        await _destinationRegistry

            .RegisterAsync(destinationPath, cancellationToken)

            .ConfigureAwait(false);



        Publish(runtime);

        await PersistHistorySafeAsync(runtime.Snapshot).ConfigureAwait(false);

        _logger.LogInformation(

            "Created transfer {SessionId} from {SourcePath} to {DestinationPath}",

            runtime.SessionId,

            sourcePath,

            destinationPath);



        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.ControlToken);

        runtime.LinkExternalCancellation(linkedCts.Token);



        try

        {

            var result = await _copyEngine.CopyAsync(

                new CopyJob(sourcePath, destinationPath, options, runtime.SessionId),

                CreateProgress(runtime),

                linkedCts.Token).ConfigureAwait(false);



            runtime.Complete(result);

            _logger.LogInformation(

                "Transfer {SessionId} completed with state {State}. Diagnostics: {Diagnostics}",

                runtime.SessionId,

                result.FinalState,

                TransferDiagnosticReport.Create(runtime.Snapshot, options, result.Duration));

        }

        catch (OperationCanceledException) when (runtime.IsPauseRequested)

        {

            runtime.MarkPaused("Transfer paused.");

            _logger.LogInformation("Transfer {SessionId} paused", runtime.SessionId);

        }

        catch (OperationCanceledException)

        {

            runtime.MarkCancelled("Transfer cancelled.");

            _logger.LogInformation("Transfer {SessionId} cancelled", runtime.SessionId);

            await CleanupCancelledSessionAsync(runtime).ConfigureAwait(false);

        }

        catch (CopyException copyException)

        {

            runtime.MarkError(CopyStateMapper.ResolveWaitingState(copyException), copyException.Message);

            _logger.LogError(

                copyException,

                "Transfer {SessionId} entered state {State}",

                runtime.SessionId,

                runtime.Snapshot.State);

        }

        catch (Exception exception)

        {

            var classified = TransientErrorClassifier.Classify(exception, "Transfer failed");

            runtime.MarkError(CopyStateMapper.ResolveWaitingState(classified), classified.Message);

            _logger.LogError(exception, "Transfer {SessionId} failed", runtime.SessionId);

        }

        finally

        {

            runtime.ClearControl();

            Publish(runtime);

            await PersistHistorySafeAsync(runtime.Snapshot).ConfigureAwait(false);

        }



        return runtime.SessionId;

    }



    public async Task ResumeAsync(

        string sessionId,

        string destinationPath,

        CopyOptions? options = null,

        CancellationToken cancellationToken = default)

    {

        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);



        await _destinationRegistry

            .RegisterAsync(destinationPath, cancellationToken)

            .ConfigureAwait(false);



        if (!_transfers.TryGetValue(sessionId, out var runtime))

        {

            runtime = TransferRuntime.FromExisting(sessionId, destinationPath);

            _transfers[sessionId] = runtime;

        }



        var (resolvedSessionId, recovery) = await ResolveResumeSessionAsync(
                runtime,
                sessionId,
                destinationPath,
                cancellationToken)
            .ConfigureAwait(false);

        EnsureRecoverableOrThrow(runtime, recovery);



        runtime.PrepareForResume(destinationPath, options);

        Publish(runtime);



        sessionId = resolvedSessionId;



        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.ControlToken);

        runtime.LinkExternalCancellation(linkedCts.Token);



        try

        {

            var result = await _copyEngine.ResumeAsync(

                sessionId,

                destinationPath,

                options ?? runtime.Options,

                CreateProgress(runtime),

                linkedCts.Token).ConfigureAwait(false);



            runtime.Complete(result);

        }

        catch (OperationCanceledException) when (runtime.IsPauseRequested)

        {

            runtime.MarkPaused("Transfer paused.");

        }

        catch (OperationCanceledException)

        {

            runtime.MarkCancelled("Transfer cancelled.");

            await CleanupCancelledSessionAsync(runtime).ConfigureAwait(false);

        }

        catch (CopyException copyException)

        {

            var shouldRestart = ShouldRestartCopyInsteadOfResume(runtime, copyException);

            if (shouldRestart)

            {

                var result = await _copyEngine.CopyAsync(

                    new CopyJob(runtime.SourcePath, destinationPath, options ?? runtime.Options, runtime.SessionId),

                    CreateProgress(runtime),

                    linkedCts.Token).ConfigureAwait(false);

                runtime.Complete(result);

            }

            else

            {

                runtime.MarkError(CopyStateMapper.ResolveWaitingState(copyException), copyException.Message);

            }

        }

        catch (Exception exception)

        {

            var classified = TransientErrorClassifier.Classify(exception, "Transfer failed");

            runtime.MarkError(CopyStateMapper.ResolveWaitingState(classified), classified.Message);

        }

        finally

        {

            runtime.ClearControl();

            Publish(runtime);

            await PersistHistorySafeAsync(runtime.Snapshot).ConfigureAwait(false);

        }

    }



    public void RequestPause(string sessionId)

    {

        if (_transfers.TryGetValue(sessionId, out var runtime))

        {

            runtime.RequestPause();

        }

    }



    public async Task CancelTransferAsync(string sessionId, CancellationToken cancellationToken = default)

    {

        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);



        if (!_transfers.TryGetValue(sessionId, out var runtime))

        {

            return;

        }



        var initialState = runtime.Snapshot.State;

        if (initialState is CopyState.Running or CopyState.Verifying or CopyState.Pending)

        {

            runtime.MarkCancelled("Transfer cancelled.");

            Publish(runtime);

            await PersistHistorySafeAsync(runtime.Snapshot).ConfigureAwait(false);

            runtime.RequestCancel();

            await WaitForCancelCompletionAsync(runtime, cancellationToken).ConfigureAwait(false);

            await CleanupCancelledSessionAsync(runtime).ConfigureAwait(false);

            return;

        }



        if (initialState is CopyState.Cancelled or CopyState.Completed)

        {

            return;

        }



        runtime.MarkCancelled("Transfer cancelled.");

        await CleanupCancelledSessionAsync(runtime).ConfigureAwait(false);

        Publish(runtime);

        await PersistHistorySafeAsync(runtime.Snapshot).ConfigureAwait(false);

    }



    private static async Task WaitForCancelCompletionAsync(

        TransferRuntime runtime,

        CancellationToken cancellationToken)

    {

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));



        while (true)

        {

            timeoutCts.Token.ThrowIfCancellationRequested();

            var state = runtime.Snapshot.State;

            if (state is CopyState.Cancelled or CopyState.Completed or CopyState.Failed)

            {

                return;

            }



            await Task.Delay(50, timeoutCts.Token).ConfigureAwait(false);

        }

    }



    public async Task DiscoverRecoverableSessionsAsync(string destinationDirectory, CancellationToken cancellationToken = default)

    {

        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);



        var recoverable = await _recoveryService

            .DiscoverRecoverableSessionsAsync(destinationDirectory, cancellationToken)

            .ConfigureAwait(false);



        foreach (var session in recoverable)

        {

            var runtime = TransferRuntime.FromRecovery(

                session.SourcePath,

                session.DestinationPath,

                session.SessionId,

                session.State,

                session.CompletedBytes,

                session.TotalBytes,

                session.CompletedChunks,

                session.TotalChunks,

                session.LastError);



            _transfers[session.SessionId] = runtime;

            Publish(runtime);

        }

    }



    public async Task RecoverSessionAsync(

        string destinationPath,

        string sessionId,

        CancellationToken cancellationToken = default)

    {

        var recovery = await _recoveryService

            .RecoverSessionAsync(destinationPath, sessionId, cancellationToken)

            .ConfigureAwait(false);



        if (recovery.Session is null)

        {

            throw new CopyException(CopyFailureKind.Permanent, recovery.Message ?? "Recovery failed.");

        }



        var runtime = TransferRuntime.FromRecovery(

            recovery.Session.SourcePath,

            recovery.Session.DestinationPath,

            recovery.Session.SessionId,

            recovery.State,

            recovery.Session.CompletedBytes,

            recovery.Session.SourceIdentity.Length,

            recovery.Session.CompletedChunkCount,

            recovery.Session.TotalChunks,

            recovery.Message);



        _transfers[recovery.Session.SessionId] = runtime;

        Publish(runtime);

    }



    public async Task RemoveTransferAsync(string sessionId, CancellationToken cancellationToken = default)

    {

        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);



        if (!_transfers.TryGetValue(sessionId, out var runtime))

        {

            return;

        }



        if (!runtime.Snapshot.CanRemove)

        {

            throw new InvalidOperationException("The transfer cannot be removed while it is active.");

        }



        await _sessionCleanupService

            .CleanupSessionAsync(runtime.DestinationPath, sessionId, cancellationToken)

            .ConfigureAwait(false);



        if (_transfers.TryRemove(sessionId, out _))

        {

            await _historyStore.RemoveAsync(sessionId, cancellationToken).ConfigureAwait(false);

            TransferRemoved?.Invoke(this, sessionId);

        }

    }



    public async Task LoadPersistedHistoryAsync(CancellationToken cancellationToken = default)

    {

        var records = await _historyStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        foreach (var record in records.OrderByDescending(static entry => entry.UpdatedUtc))

        {

            cancellationToken.ThrowIfCancellationRequested();



            if (_transfers.ContainsKey(record.SessionId))

            {

                continue;

            }



            var runtime = TransferRuntime.FromRecovery(

                record.SourcePath,

                record.DestinationPath,

                record.SessionId,

                record.State,

                record.BytesCopied,

                record.TotalBytes,

                record.CompletedChunks,

                record.TotalChunks,

                record.ErrorMessage);



            _transfers[record.SessionId] = runtime;

            Publish(runtime);

        }



        var registeredDestinations = await _destinationRegistry

            .GetRegisteredAsync(cancellationToken)

            .ConfigureAwait(false);



        var destinationPaths = records

            .Select(static record => record.DestinationPath)

            .Concat(registeredDestinations)

            .Where(static path => !string.IsNullOrWhiteSpace(path))

            .Distinct(StringComparer.OrdinalIgnoreCase)



            .ToArray();



        foreach (var destinationPath in destinationPaths)

        {

            cancellationToken.ThrowIfCancellationRequested();

            await DiscoverAndMergeOrphanedSessionsAsync(destinationPath, cancellationToken).ConfigureAwait(false);

        }

    }



    public async Task ClearFinishedTransfersAsync(CancellationToken cancellationToken = default)

    {

        var removable = _transfers.Values

            .Where(static runtime => runtime.Snapshot.CanRemove)

            .Select(static runtime => runtime.SessionId)

            .ToArray();



        foreach (var sessionId in removable)

        {

            cancellationToken.ThrowIfCancellationRequested();

            await RemoveTransferAsync(sessionId, cancellationToken).ConfigureAwait(false);

        }

    }



    public void NotifyVolumesChanged()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _driveProvider.InvalidateReadinessCache();
        CheckWaitingTransfers(null);
    }



    public void Dispose()

    {

        if (Interlocked.Exchange(ref _disposed, 1) != 0)

        {

            return;

        }



        _reconnectTimer.Dispose();

    }



    private async Task CleanupCancelledSessionAsync(TransferRuntime runtime)

    {

        await _sessionCleanupService

            .CleanupSessionAsync(runtime.DestinationPath, runtime.SessionId, CancellationToken.None)

            .ConfigureAwait(false);

    }



    private async Task<(string SessionId, RecoveryResult Recovery)> ResolveResumeSessionAsync(
        TransferRuntime runtime,
        string sessionId,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var recovery = await _recoveryService
            .RecoverSessionAsync(destinationPath, sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (recovery.CanResume
            || recovery.Message is null
            || !recovery.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return (sessionId, recovery);
        }

        IReadOnlyList<RecoverableSessionInfo> recoverable;
        try
        {
            recoverable = await _recoveryService
                .DiscoverRecoverableSessionsAsync(destinationPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DestinationUnavailableException)
        {
            throw;
        }
        catch (SourceUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not discover recoverable sessions for {DestinationPath}", destinationPath);
            return (sessionId, recovery);
        }

        var match = recoverable.FirstOrDefault(session =>
            string.Equals(session.SourcePath, runtime.SourcePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(session.DestinationPath, destinationPath, StringComparison.OrdinalIgnoreCase));

        if (match is null || string.Equals(match.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
        {
            return (sessionId, recovery);
        }

        RebindTransferSession(runtime, match.SessionId);
        sessionId = match.SessionId;
        recovery = await _recoveryService
            .RecoverSessionAsync(destinationPath, sessionId, cancellationToken)
            .ConfigureAwait(false);

        return (sessionId, recovery);
    }

    private void EnsureRecoverableOrThrow(TransferRuntime runtime, RecoveryResult recovery)
    {
        if (recovery.CanResume && recovery.Session is not null)
        {
            return;
        }

        var message = recovery.Message ?? $"Session '{recovery.SessionId}' cannot be resumed.";
        runtime.MarkError(RecoveryFailureMapper.ResolveWaitingState(recovery), message);
        Publish(runtime);
        throw RecoveryFailureMapper.CreateException(recovery);
    }

    private void RebindTransferSession(TransferRuntime runtime, string newSessionId)
    {
        if (string.Equals(runtime.SessionId, newSessionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var oldSessionId = runtime.SessionId;
        _transfers.TryRemove(oldSessionId, out _);
        runtime.RebindSessionId(newSessionId);
        _transfers[newSessionId] = runtime;
        _ = _historyStore.RemoveAsync(oldSessionId, CancellationToken.None);
        Publish(runtime);
    }

    private static bool ShouldRestartCopyInsteadOfResume(TransferRuntime runtime, CopyException copyException) =>

        runtime.Snapshot.BytesCopied == 0

        && runtime.Snapshot.CompletedChunks == 0

        && !string.IsNullOrWhiteSpace(runtime.SourcePath)

        && !string.Equals(runtime.SourcePath, "Unknown", StringComparison.OrdinalIgnoreCase)

        && copyException.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);



    private void CheckWaitingTransfers(object? state)

    {

        if (Volatile.Read(ref _disposed) != 0)

        {

            return;

        }



        if (!_transfers.Values.Any(static runtime => IsWaitingForDeviceState(runtime.Snapshot.State)))

        {

            return;

        }



        _driveProvider.InvalidateReadinessCache();



        foreach (var runtime in _transfers.Values)

        {

            if (!TryResolveWaitingTransfer(runtime))

            {

                continue;

            }



            var waitingSnapshot = runtime.Snapshot;

            runtime.MarkPaused(
                waitingSnapshot.BytesCopied == 0 && waitingSnapshot.CompletedChunks == 0
                    ? "Destination is ready. Press Resume to start."
                    : "Device reconnected. Press Resume.");

            Publish(runtime);

            _logger.LogInformation(

                "Transfer {SessionId} detected device reconnection and is ready to resume",

                runtime.SessionId);

        }

    }



    private static bool IsWaitingForDeviceState(CopyState state) =>

        state is CopyState.WaitingForSource

            or CopyState.WaitingForDestination

            or CopyState.WaitingForStorage;



    private bool TryResolveWaitingTransfer(TransferRuntime runtime)

    {

        var snapshot = runtime.Snapshot;

        if (!IsWaitingForDeviceState(snapshot.State))

        {

            return false;

        }



        _driveProvider.InvalidateReadinessCache(snapshot.SourcePath);

        _driveProvider.InvalidateReadinessCache(snapshot.DestinationPath);



        return snapshot.State switch

        {

            CopyState.WaitingForSource =>

                _deviceMonitor.IsVolumeReady(snapshot.SourcePath)

                    && _deviceMonitor.IsPathAccessible(snapshot.SourcePath)

                    && _fileSystemService.FileExists(snapshot.SourcePath),

            CopyState.WaitingForDestination =>

                _deviceMonitor.IsVolumeReady(snapshot.DestinationPath)

                    && _deviceMonitor.IsPathAccessible(snapshot.DestinationPath),

            CopyState.WaitingForStorage =>

                _deviceMonitor.IsVolumeReady(snapshot.DestinationPath)

                    && _deviceMonitor.IsPathAccessible(snapshot.DestinationPath),

            _ => false

        };

    }



    private IProgress<CopyProgress> CreateProgress(TransferRuntime runtime)

    {

        var reporter = new ThrottledProgressReporter(

            new DelegateProgress<CopyProgress>(progress =>

            {

                runtime.ApplyProgress(progress);

                Publish(runtime);

            }),

            _progressInterval);



        runtime.SetProgressFlush(reporter.Flush);

        return reporter;

    }



    private void Publish(TransferRuntime runtime)

    {

        var snapshot = runtime.Snapshot;

        _logger.LogDebug(

            "Transfer {SessionId} state transition to {State} ({CompletedBytes}/{TotalBytes} bytes)",

            snapshot.SessionId,

            snapshot.State,

            snapshot.BytesCopied,

            snapshot.TotalBytes);

        TransferChanged?.Invoke(this, snapshot);

        if (ShouldPersistToHistory(snapshot.State))

        {

            _ = PersistHistorySafeAsync(snapshot);

        }

    }



    private static bool ShouldPersistToHistory(CopyState state) =>

        state is CopyState.Pending

            or CopyState.Completed

            or CopyState.Failed

            or CopyState.Cancelled

            or CopyState.Paused

            or CopyState.WaitingForSource

            or CopyState.WaitingForDestination

            or CopyState.WaitingForStorage

            or CopyState.RecoveryRequired;



    private async Task DiscoverAndMergeOrphanedSessionsAsync(

        string destinationPath,

        CancellationToken cancellationToken)

    {

        IReadOnlyList<RecoverableSessionInfo> recoverable;

        try

        {

            recoverable = await _recoveryService

                .DiscoverRecoverableSessionsAsync(destinationPath, cancellationToken)

                .ConfigureAwait(false);

        }

        catch (DestinationUnavailableException exception)

        {

            _logger.LogDebug(

                exception,

                "Skipping orphaned session discovery for unavailable destination {DestinationPath}",

                destinationPath);

            return;

        }



        foreach (var session in recoverable)

        {

            cancellationToken.ThrowIfCancellationRequested();



            if (_transfers.TryGetValue(session.SessionId, out var existing)

                && existing.Snapshot.State is CopyState.Cancelled or CopyState.Completed)

            {

                continue;

            }



            var runtime = TransferRuntime.FromRecovery(

                session.SourcePath,

                session.DestinationPath,

                session.SessionId,

                session.State,

                session.CompletedBytes,

                session.TotalBytes,

                session.CompletedChunks,

                session.TotalChunks,

                session.LastError);



            _transfers[session.SessionId] = runtime;

            Publish(runtime);

        }

    }



    private async Task PersistHistorySafeAsync(TransferSnapshot snapshot)

    {

        try

        {

            await _historyStore

                .UpsertAsync(TransferHistoryRecord.FromSnapshot(snapshot), CancellationToken.None)

                .ConfigureAwait(false);

        }

        catch (Exception exception)

        {

            _logger.LogWarning(exception, "Failed to persist transfer history for {SessionId}", snapshot.SessionId);

        }

    }



    private sealed class TransferRuntime

    {

        private readonly object _sync = new();

        private CopyOptions _options;

        private CopyState _state = CopyState.Pending;

        private long _bytesCopied;

        private long _totalBytes;

        private int _completedChunks;

        private int _totalChunks;

        private string? _errorMessage;

        private DateTimeOffset _lastProgressUtc = DateTimeOffset.UtcNow;

        private long _lastBytesCopied;

        private double _bytesPerSecond;

        private Action? _flushProgress;



        public TransferRuntime(string sourcePath, string destinationPath, CopyOptions options, string? sessionId = null)

        {

            SourcePath = sourcePath;

            DestinationPath = destinationPath;

            _options = options;

            SessionId = sessionId ?? Guid.NewGuid().ToString("N");

            ControlSource = new CancellationTokenSource();

        }



        public void RebindSessionId(string sessionId)

        {

            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            lock (_sync)

            {

                SessionId = sessionId;

            }

        }



        public static TransferRuntime FromRecovery(

            string sourcePath,

            string destinationPath,

            string sessionId,

            CopyState state,

            long bytesCopied,

            long totalBytes,

            int completedChunks,

            int totalChunks,

            string? errorMessage)

        {

            var runtime = new TransferRuntime(sourcePath, destinationPath, new CopyOptions(), sessionId);

            runtime.ApplyRecoveredState(state, bytesCopied, totalBytes, completedChunks, totalChunks, errorMessage);

            return runtime;

        }



        public void ApplyRecoveredState(

            CopyState state,

            long bytesCopied,

            long totalBytes,

            int completedChunks,

            int totalChunks,

            string? errorMessage)

        {

            lock (_sync)

            {

                _state = state;

                _bytesCopied = bytesCopied;

                _totalBytes = totalBytes;

                _completedChunks = completedChunks;

                _totalChunks = totalChunks;

                _errorMessage = errorMessage;

            }

        }



        public string SessionId { get; private set; }



        public string SourcePath { get; private set; }



        public string DestinationPath { get; private set; }



        public CopyOptions Options => _options;



        public CancellationTokenSource ControlSource { get; private set; }



        public CancellationToken ControlToken => ControlSource.Token;



        public bool IsPauseRequested { get; private set; }



        public TransferSnapshot Snapshot

        {

            get

            {

                lock (_sync)

                {

                    return BuildSnapshot();

                }

            }

        }



        public static TransferRuntime FromExisting(string sessionId, string destinationPath)

        {

            var runtime = new TransferRuntime("Unknown", destinationPath, new CopyOptions(), sessionId);

            runtime.MarkPaused("Transfer paused.");

            return runtime;

        }



        public void LinkExternalCancellation(CancellationToken token)

        {

            token.Register(() =>

            {

                if (IsPauseRequested)

                {

                    return;

                }



                ControlSource.Cancel();

            });

        }



        public void SetProgressFlush(Action flush) => _flushProgress = flush;



        public void PrepareForResume(string destinationPath, CopyOptions? options)

        {

            lock (_sync)

            {

                DestinationPath = destinationPath;

                if (options is not null)

                {

                    _options = options;

                }



                _state = CopyState.Running;

                _errorMessage = null;

                IsPauseRequested = false;

                ControlSource = new CancellationTokenSource();

            }

        }



        public void ApplyProgress(CopyProgress progress)

        {

            lock (_sync)

            {

                if (!CopyProgressGuard.ShouldApplyProgress(_state, progress.State))

                {

                    return;

                }



                var now = DateTimeOffset.UtcNow;

                var elapsedSeconds = (now - _lastProgressUtc).TotalSeconds;

                if (elapsedSeconds > 0.01d)

                {

                    var deltaBytes = progress.BytesCopied - _lastBytesCopied;

                    _bytesPerSecond = deltaBytes / elapsedSeconds;

                    _lastProgressUtc = now;

                    _lastBytesCopied = progress.BytesCopied;

                }



                _state = progress.State;

                _bytesCopied = progress.BytesCopied;

                _totalBytes = progress.TotalBytes;

                _completedChunks = progress.CompletedChunks;

                _totalChunks = progress.TotalChunks;

            }

        }



        public void Complete(CopyResult result)

        {

            lock (_sync)

            {

                _state = result.FinalState;

                _bytesCopied = result.BytesCopied;

                _totalBytes = result.BytesCopied;

                _errorMessage = null;

                _bytesPerSecond = 0;

            }



            _flushProgress?.Invoke();

        }



        public void MarkPaused(string message)

        {

            lock (_sync)

            {

                _state = CopyState.Paused;

                _errorMessage = message;

                _bytesPerSecond = 0;

            }



            _flushProgress?.Invoke();

        }



        public void MarkCancelled(string message)

        {

            lock (_sync)

            {

                _state = CopyState.Cancelled;

                _errorMessage = message;

                _bytesPerSecond = 0;

            }

        }



        public void MarkError(CopyState state, string message)

        {

            lock (_sync)

            {

                _state = state;

                _errorMessage = message;

                _bytesPerSecond = 0;

            }



            _flushProgress?.Invoke();

        }



        public void RequestPause()

        {

            IsPauseRequested = true;

            ControlSource.Cancel();

        }



        public void RequestCancel()

        {

            IsPauseRequested = false;

            ControlSource.Cancel();

        }



        public void ClearControl()

        {

            IsPauseRequested = false;

        }



        private TransferSnapshot BuildSnapshot()

        {

            TimeSpan? eta = null;

            if (_bytesPerSecond > 1d && _totalBytes > _bytesCopied)

            {

                var remainingBytes = _totalBytes - _bytesCopied;

                eta = TimeSpan.FromSeconds(remainingBytes / _bytesPerSecond);

            }



            var isActive = _state is CopyState.Running or CopyState.Verifying or CopyState.Pending;

            var canPause = _state is CopyState.Running or CopyState.Verifying;

            var canResume = _state is CopyState.Paused

                or CopyState.WaitingForSource

                or CopyState.WaitingForDestination

                or CopyState.WaitingForStorage;

            var canRetry = _state is CopyState.RecoveryRequired or CopyState.Failed;

            var canCancel = canPause || canResume;

            var canRemove = !isActive;



            return new TransferSnapshot

            {

                SessionId = SessionId,

                SourcePath = SourcePath,

                DestinationPath = DestinationPath,

                State = _state,

                BytesCopied = _bytesCopied,

                TotalBytes = _totalBytes,

                CompletedChunks = _completedChunks,

                TotalChunks = _totalChunks,

                BytesPerSecond = _bytesPerSecond,

                EstimatedTimeRemaining = eta,

                StatusText = UserMessageFormatter.GetStatusText(_state),

                ErrorMessage = _errorMessage,

                CanPause = canPause,

                CanResume = canResume || canRetry,

                CanCancel = canCancel,

                CanRetry = canRetry,

                CanRemove = canRemove

            };

        }

    }



    private sealed class DelegateProgress<T> : IProgress<T>

    {

        private readonly Action<T> _handler;



        public DelegateProgress(Action<T> handler)

        {

            _handler = handler ?? throw new ArgumentNullException(nameof(handler));

        }



        public void Report(T value) => _handler(value);

    }

}

