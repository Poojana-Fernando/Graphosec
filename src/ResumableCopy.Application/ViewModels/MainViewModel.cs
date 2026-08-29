using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Configuration;
using ResumableCopy.Application.Diagnostics;
using ResumableCopy.Application.Models;
using ResumableCopy.Application.Services;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Application.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ITransferOrchestrator _orchestrator;
    private readonly IFilePickerService _filePickerService;
    private readonly IUiThread _uiThread;
    private readonly IBackgroundExecutor _backgroundExecutor;
    private readonly IPathAnalysisService _pathAnalysisService;
    private readonly IUserPromptService _userPromptService;
    private readonly IThemeService? _themeService;
    private readonly IDriveEnumerationService? _driveEnumerationService;
    private readonly IVolumeChangeMonitor? _volumeChangeMonitor;
    private readonly ILogger<MainViewModel> _logger;
    private readonly ResumableCopySettings _settings;
    private readonly Dictionary<string, TransferItemViewModel> _transferLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TransferSnapshot> _pendingSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _snapshotSync = new();
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _sourceAnalysisCancellation;
    private CancellationTokenSource? _destinationAnalysisCancellation;
    private int _sourceAnalysisGeneration;
    private int _destinationAnalysisGeneration;
    private bool _snapshotFlushScheduled;
    private bool _disposed;

    public MainViewModel(
        ITransferOrchestrator orchestrator,
        IFilePickerService filePickerService,
        IUiThread uiThread,
        IBackgroundExecutor backgroundExecutor,
        IPathAnalysisService pathAnalysisService,
        IOptions<ResumableCopySettings> settings,
        ILogger<MainViewModel>? logger = null,
        IVolumeChangeMonitor? volumeChangeMonitor = null,
        IDriveEnumerationService? driveEnumerationService = null,
        IUserPromptService? userPromptService = null,
        IThemeService? themeService = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _filePickerService = filePickerService ?? throw new ArgumentNullException(nameof(filePickerService));
        _uiThread = uiThread ?? throw new ArgumentNullException(nameof(uiThread));
        _backgroundExecutor = backgroundExecutor ?? throw new ArgumentNullException(nameof(backgroundExecutor));
        _pathAnalysisService = pathAnalysisService ?? throw new ArgumentNullException(nameof(pathAnalysisService));
        _userPromptService = userPromptService ?? new DefaultUserPromptService();
        _themeService = themeService;
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? NullLogger<MainViewModel>.Instance;
        _volumeChangeMonitor = volumeChangeMonitor;
        _driveEnumerationService = driveEnumerationService;

        _orchestrator.TransferChanged += OnTransferChanged;
        _orchestrator.TransferRemoved += OnTransferRemoved;

        if (_volumeChangeMonitor is not null)
        {
            _volumeChangeMonitor.VolumesChanged += OnVolumesChanged;
        }

        SelectedTheme = _themeService?.CurrentTheme ?? AppTheme.Light;

        _ = InitializeAsync();
    }

    public ObservableCollection<DriveItemViewModel> Drives { get; } = [];

    public ObservableCollection<TransferItemViewModel> Transfers { get; } = [];

    public ObservableCollection<TransferItemViewModel> CompletedTransfers { get; } = [];

    public ObservableCollection<TransferItemViewModel> PausedTransfers { get; } = [];

    public ObservableCollection<TransferItemViewModel> CancelledTransfers { get; } = [];

    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private string _sourceInfoText = string.Empty;

    [ObservableProperty]
    private string _destinationInfoText = string.Empty;

    [ObservableProperty]
    private bool _overwriteExisting;

    [ObservableProperty]
    private TransferItemViewModel? _selectedTransfer;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private AppTheme _selectedTheme = AppTheme.Light;

    public bool IsLightThemeSelected
    {
        get => SelectedTheme == AppTheme.Light;
        set
        {
            if (value)
            {
                SelectedTheme = AppTheme.Light;
            }
        }
    }

    public bool IsDarkThemeSelected
    {
        get => SelectedTheme == AppTheme.Dark;
        set
        {
            if (value)
            {
                SelectedTheme = AppTheme.Dark;
            }
        }
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        OnPropertyChanged(nameof(IsLightThemeSelected));
        OnPropertyChanged(nameof(IsDarkThemeSelected));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        SelectedTheme = _themeService?.CurrentTheme ?? SelectedTheme;
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [RelayCommand]
    private void ApplySettings()
    {
        _themeService?.ApplyTheme(SelectedTheme);
        IsSettingsOpen = false;
        StatusMessage = SelectedTheme == AppTheme.Dark ? "Dark mode enabled." : "Light mode enabled.";
    }

    [RelayCommand(CanExecute = nameof(CanStartTransfer))]
    private async Task StartTransferAsync()
    {
        IsBusy = true;
        StatusMessage = "Starting transfer...";
        var cts = ReplaceOperationCancellation();
        var sourcePath = SourcePath;
        var destinationPath = DestinationPath;
        var options = _settings.Copy.ToCopyOptions(OverwriteExisting);

        try
        {
            string sessionId;
            using (OperationTimer.Measure(_logger, "StartCopy"))
            {
                sessionId = await _backgroundExecutor.RunAsync(
                    token => _orchestrator.StartCopyAsync(sourcePath, destinationPath, options, token),
                    cts.Token).ConfigureAwait(false);
            }

            _uiThread.Invoke(() =>
            {
                var snapshot = _orchestrator.GetTransfer(sessionId);
                StatusMessage = snapshot is null
                    ? "Transfer operation finished."
                    : FormatOperationFinishedMessage(snapshot);
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Start transfer failed.");
            _uiThread.Invoke(() => StatusMessage = UserMessageFormatter.GetUserMessage(exception));
        }
        finally
        {
            ClearOperationCancellation(cts);
            _uiThread.Invoke(() => IsBusy = false);
        }
    }

    private bool CanStartTransfer() =>
        !IsBusy
        && !string.IsNullOrWhiteSpace(SourcePath)
        && !string.IsNullOrWhiteSpace(DestinationPath);

    [RelayCommand(CanExecute = nameof(CanPauseSelectedTransfer))]
    private void PauseSelectedTransfer()
    {
        if (SelectedTransfer is null)
        {
            return;
        }

        _orchestrator.RequestPause(SelectedTransfer.SessionId);
        StatusMessage = "Pausing transfer...";
    }

    private bool CanPauseSelectedTransfer() => SelectedTransfer?.CanPause == true;

    [RelayCommand(CanExecute = nameof(CanResumeSelectedTransfer))]
    private async Task ResumeSelectedTransferAsync()
    {
        if (SelectedTransfer is null)
        {
            return;
        }

        IsBusy = true;
        var cts = ReplaceOperationCancellation();
        var sessionId = SelectedTransfer.SessionId;
        var destinationPath = SelectedTransfer.DestinationPath;
        var options = _settings.Copy.ToCopyOptions(OverwriteExisting);
        StatusMessage = "Resuming transfer...";

        try
        {
            using (OperationTimer.Measure(_logger, "ResumeCopy"))
            {
                await _backgroundExecutor.RunAsync(
                    token => _orchestrator.ResumeAsync(sessionId, destinationPath, options, token),
                    cts.Token).ConfigureAwait(false);
            }

            _uiThread.Invoke(() =>
            {
                var snapshot = _orchestrator.GetTransfer(sessionId);
                StatusMessage = snapshot is null
                    ? "Transfer operation finished."
                    : FormatOperationFinishedMessage(snapshot);
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Resume transfer failed.");
            _uiThread.Invoke(() => StatusMessage = UserMessageFormatter.GetUserMessage(exception));
        }
        finally
        {
            ClearOperationCancellation(cts);
            _uiThread.Invoke(() => IsBusy = false);
        }
    }

    private bool CanResumeSelectedTransfer() => SelectedTransfer?.CanResume == true;

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedTransfer))]
    private async Task RemoveSelectedTransferAsync()
    {
        if (SelectedTransfer is null)
        {
            return;
        }

        IsBusy = true;
        var sessionId = SelectedTransfer.SessionId;
        StatusMessage = "Removing transfer...";

        try
        {
            await _backgroundExecutor.RunAsync(
                token => _orchestrator.RemoveTransferAsync(sessionId, token),
                CancellationToken.None).ConfigureAwait(false);
            _uiThread.Invoke(() => StatusMessage = "Transfer removed.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Remove transfer failed.");
            _uiThread.Invoke(() => StatusMessage = UserMessageFormatter.GetUserMessage(exception));
        }
        finally
        {
            _uiThread.Invoke(() => IsBusy = false);
        }
    }

    private bool CanRemoveSelectedTransfer() => SelectedTransfer?.CanRemove == true && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanClearHistory))]
    private async Task ClearHistoryAsync()
    {
        IsBusy = true;
        StatusMessage = "Clearing transfer history...";

        try
        {
            await _backgroundExecutor.RunAsync(
                token => _orchestrator.ClearFinishedTransfersAsync(token),
                CancellationToken.None).ConfigureAwait(false);
            _uiThread.Invoke(() => StatusMessage = "Transfer history cleared.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Clear history failed.");
            _uiThread.Invoke(() => StatusMessage = UserMessageFormatter.GetUserMessage(exception));
        }
        finally
        {
            _uiThread.Invoke(() => IsBusy = false);
        }
    }

    private bool CanClearHistory() => !IsBusy && Transfers.Any(static transfer => transfer.CanRemove);

    [RelayCommand(CanExecute = nameof(CanCancelSelectedTransfer))]
    private async Task CancelSelectedTransferAsync()
    {
        if (SelectedTransfer is null)
        {
            return;
        }

        var confirmed = _userPromptService.Confirm(
            "Cancel transfer",
            "Are you sure you want to cancel this transfer? Any partial files will be removed.");

        if (!confirmed)
        {
            return;
        }

        var sessionId = SelectedTransfer.SessionId;
        StatusMessage = "Cancelling transfer...";

        try
        {
            await _backgroundExecutor.RunAsync(
                token => _orchestrator.CancelTransferAsync(sessionId, token),
                CancellationToken.None).ConfigureAwait(false);

            _uiThread.Invoke(() =>
            {
                var snapshot = _orchestrator.GetTransfer(sessionId);
                StatusMessage = snapshot?.State == CopyState.Cancelled
                    ? "Transfer cancelled."
                    : snapshot is null
                        ? "Transfer cancelled."
                        : FormatOperationFinishedMessage(snapshot);
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Cancel transfer failed.");
            _uiThread.Invoke(() => StatusMessage = UserMessageFormatter.GetUserMessage(exception));
        }
    }

    private bool CanCancelSelectedTransfer() => SelectedTransfer?.CanCancel == true;

    [RelayCommand(CanExecute = nameof(CanRecoverSessions))]
    private async Task RecoverSessionsAsync()
    {
        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            StatusMessage = "Choose a destination path to scan for recoverable transfers.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Scanning for recoverable transfers...";
        var cts = ReplaceOperationCancellation();
        var destinationPath = DestinationPath;

        try
        {
            await _backgroundExecutor.RunAsync(
                token => _orchestrator.DiscoverRecoverableSessionsAsync(destinationPath, token),
                cts.Token).ConfigureAwait(false);
            _uiThread.Invoke(() => StatusMessage = "Recovery scan completed.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Recovery scan failed.");
            _uiThread.Invoke(() => StatusMessage = UserMessageFormatter.GetUserMessage(exception));
        }
        finally
        {
            ClearOperationCancellation(cts);
            _uiThread.Invoke(() => IsBusy = false);
        }
    }

    private bool CanRecoverSessions() => !IsBusy && !string.IsNullOrWhiteSpace(DestinationPath);

    [RelayCommand]
    private void BrowseSource()
    {
        using (OperationTimer.Measure(_logger, "FileSelection"))
        {
            var path = _filePickerService.PickSourceFile();
            if (!string.IsNullOrWhiteSpace(path))
            {
                SourcePath = path;
            }
        }
    }

    [RelayCommand]
    private void BrowseDestination()
    {
        using (OperationTimer.Measure(_logger, "FileSelection"))
        {
            var path = _filePickerService.PickDestinationFile(SourcePath);
            if (!string.IsNullOrWhiteSpace(path))
            {
                DestinationPath = path;
            }
        }
    }

    [RelayCommand]
    private void RefreshDrives()
    {
        _ = RefreshDrivesAsync();
    }

    [RelayCommand]
    private void SelectDrive(DriveItemViewModel? drive)
    {
        if (drive is null || !drive.IsReady)
        {
            return;
        }

        DestinationPath = drive.RootPath;
        RecoverSessionsCommand.NotifyCanExecuteChanged();
        _ = AnalyzeDestinationAsync(DestinationPath);
    }

    partial void OnSourcePathChanged(string value)
    {
        StartTransferCommand.NotifyCanExecuteChanged();
        _ = AnalyzeSourceAsync(value);
    }

    partial void OnDestinationPathChanged(string value)
    {
        StartTransferCommand.NotifyCanExecuteChanged();
        RecoverSessionsCommand.NotifyCanExecuteChanged();
        _ = AnalyzeDestinationAsync(value);
    }

    partial void OnIsBusyChanged(bool value)
    {
        StartTransferCommand.NotifyCanExecuteChanged();
        PauseSelectedTransferCommand.NotifyCanExecuteChanged();
        ResumeSelectedTransferCommand.NotifyCanExecuteChanged();
        RecoverSessionsCommand.NotifyCanExecuteChanged();
        RemoveSelectedTransferCommand.NotifyCanExecuteChanged();
        ClearHistoryCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTransferChanged(TransferItemViewModel? value)
    {
        PauseSelectedTransferCommand.NotifyCanExecuteChanged();
        ResumeSelectedTransferCommand.NotifyCanExecuteChanged();
        CancelSelectedTransferCommand.NotifyCanExecuteChanged();
        RemoveSelectedTransferCommand.NotifyCanExecuteChanged();
    }

    private void OnTransferChanged(object? sender, TransferSnapshot snapshot)
    {
        lock (_snapshotSync)
        {
            _pendingSnapshots[snapshot.SessionId] = snapshot;
            if (_snapshotFlushScheduled)
            {
                return;
            }

            _snapshotFlushScheduled = true;
            _uiThread.Post(FlushPendingSnapshots);
        }
    }

    private void FlushPendingSnapshots()
    {
        TransferSnapshot[] snapshots;
        lock (_snapshotSync)
        {
            snapshots = [.. _pendingSnapshots.Values];
            _pendingSnapshots.Clear();
            _snapshotFlushScheduled = false;
        }

        foreach (var snapshot in snapshots)
        {
            ApplySnapshot(snapshot);
        }
    }

    private void ApplySnapshot(TransferSnapshot snapshot)
    {
        CopyState previousState;
        if (!_transferLookup.TryGetValue(snapshot.SessionId, out var item))
        {
            item = new TransferItemViewModel { SessionId = snapshot.SessionId };
            _transferLookup[snapshot.SessionId] = item;
            Transfers.Add(item);
            previousState = CopyState.Pending;
        }
        else
        {
            previousState = item.State;
        }

        item.SourcePath = snapshot.SourcePath;
        item.DestinationPath = snapshot.DestinationPath;
        item.State = snapshot.State;
        item.StatusText = snapshot.StatusText;
        item.ProgressPercent = snapshot.ProgressPercent;
        item.BytesCopiedText = FormatBytes(snapshot.BytesCopied, snapshot.TotalBytes);
        item.SpeedText = FormatSpeed(snapshot.BytesPerSecond);
        item.EtaText = FormatEta(snapshot.EstimatedTimeRemaining);
        item.ErrorMessage = snapshot.ErrorMessage;
        item.UserMessage = UserMessageFormatter.GetUserMessage(snapshot.State, snapshot.ErrorMessage);
        item.CanPause = snapshot.CanPause;
        item.CanResume = snapshot.CanResume;
        item.CanCancel = snapshot.CanCancel;
        item.CanRetry = snapshot.CanRetry;
        item.CanRemove = snapshot.CanRemove;

        SyncFilteredCollections(item, previousState);

        ClearHistoryCommand.NotifyCanExecuteChanged();

        if (SelectedTransfer?.SessionId == snapshot.SessionId)
        {
            PauseSelectedTransferCommand.NotifyCanExecuteChanged();
            ResumeSelectedTransferCommand.NotifyCanExecuteChanged();
            CancelSelectedTransferCommand.NotifyCanExecuteChanged();
            RemoveSelectedTransferCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnVolumesChanged(object? sender, EventArgs e)
    {
        _ = RefreshDrivesAsync();

        if (!string.IsNullOrWhiteSpace(DestinationPath))
        {
            _ = AnalyzeDestinationAsync(DestinationPath);
        }

        if (!string.IsNullOrWhiteSpace(SourcePath))
        {
            _ = AnalyzeSourceAsync(SourcePath);
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _backgroundExecutor.RunAsync(
                token => _orchestrator.LoadPersistedHistoryAsync(token),
                CancellationToken.None).ConfigureAwait(false);

            await RefreshDrivesAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Startup initialization failed.");
        }
    }

    private async Task RefreshDrivesAsync()
    {
        if (_driveEnumerationService is null)
        {
            return;
        }

        try
        {
            var drives = await _backgroundExecutor.RunAsync(
                _ => Task.FromResult(_driveEnumerationService.GetAvailableDrives()),
                CancellationToken.None).ConfigureAwait(false);

            _uiThread.Invoke(() =>
            {
                Drives.Clear();
                foreach (var drive in drives)
                {
                    Drives.Add(new DriveItemViewModel(drive));
                }
            });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Drive enumeration failed.");
        }
    }

    private void OnTransferRemoved(object? sender, string sessionId)
    {
        _uiThread.Post(() =>
        {
            if (_transferLookup.Remove(sessionId, out var item))
            {
                Transfers.Remove(item);
                RemoveFromFilteredCollections(item);
            }

            if (SelectedTransfer?.SessionId == sessionId)
            {
                SelectedTransfer = null;
            }

            ClearHistoryCommand.NotifyCanExecuteChanged();
        });
    }

    private void SyncFilteredCollections(TransferItemViewModel item, CopyState previousState)
    {
        if (previousState != item.State)
        {
            RemoveFromFilteredCollections(item);
        }

        AddToFilteredCollection(item);
    }

    private void RemoveFromFilteredCollections(TransferItemViewModel item)
    {
        CompletedTransfers.Remove(item);
        PausedTransfers.Remove(item);
        CancelledTransfers.Remove(item);
    }

    private void AddToFilteredCollection(TransferItemViewModel item)
    {
        switch (item.State)
        {
            case CopyState.Completed when !CompletedTransfers.Contains(item):
                CompletedTransfers.Add(item);
                break;
            case CopyState.Paused when !PausedTransfers.Contains(item):
                PausedTransfers.Add(item);
                break;
            case CopyState.Cancelled when !CancelledTransfers.Contains(item):
                CancelledTransfers.Add(item);
                break;
        }
    }

    private static string FormatOperationFinishedMessage(TransferSnapshot snapshot) =>
        snapshot.State switch
        {
            CopyState.WaitingForDestination =>
                "Connect the destination storage device and press Resume to continue.",
            CopyState.WaitingForSource =>
                "Waiting for source. Reconnect the source and press Resume.",
            CopyState.WaitingForStorage =>
                "Insufficient space. Free space on the destination and press Resume.",
            CopyState.Completed => "Transfer completed successfully.",
            CopyState.Cancelled => "Transfer cancelled.",
            CopyState.Failed => UserMessageFormatter.GetUserMessage(snapshot.State, snapshot.ErrorMessage),
            CopyState.Paused when snapshot.ErrorMessage?.Contains("reconnected", StringComparison.OrdinalIgnoreCase) == true =>
                "Device reconnected. Press Resume to continue.",
            CopyState.Paused => "Transfer paused. Press Resume to continue.",
            _ => "Transfer operation finished."
        };

    private async Task AnalyzeSourceAsync(string path)
    {
        var generation = Interlocked.Increment(ref _sourceAnalysisGeneration);
        using var cts = ReplaceAnalysisCancellation(ref _sourceAnalysisCancellation);

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                PostSourceInfo(generation, string.Empty);
                return;
            }

            var analysis = await _backgroundExecutor.RunAsync(
                token => _pathAnalysisService.AnalyzeSourceAsync(path, token),
                cts.Token).ConfigureAwait(false);

            if (generation != Volatile.Read(ref _sourceAnalysisGeneration))
            {
                return;
            }

            PostSourceInfo(generation, FormatSourceAnalysis(analysis));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Source path analysis failed.");
            if (generation == Volatile.Read(ref _sourceAnalysisGeneration))
            {
                PostSourceInfo(generation, "Unable to analyze source path.");
            }
        }
    }

    private async Task AnalyzeDestinationAsync(string path)
    {
        var generation = Interlocked.Increment(ref _destinationAnalysisGeneration);
        using var cts = ReplaceAnalysisCancellation(ref _destinationAnalysisCancellation);

        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                PostDestinationInfo(generation, string.Empty);
                return;
            }

            var analysis = await _backgroundExecutor.RunAsync(
                token => _pathAnalysisService.AnalyzeDestinationAsync(path, token),
                cts.Token).ConfigureAwait(false);

            if (generation != Volatile.Read(ref _destinationAnalysisGeneration))
            {
                return;
            }

            PostDestinationInfo(generation, FormatDestinationAnalysis(analysis));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Destination path analysis failed.");
            if (generation == Volatile.Read(ref _destinationAnalysisGeneration))
            {
                PostDestinationInfo(generation, "Unable to analyze destination path.");
            }
        }
    }

    private void PostSourceInfo(int generation, string text)
    {
        _uiThread.Post(() =>
        {
            if (generation == Volatile.Read(ref _sourceAnalysisGeneration))
            {
                SourceInfoText = text;
            }
        });
    }

    private void PostDestinationInfo(int generation, string text)
    {
        _uiThread.Post(() =>
        {
            if (generation == Volatile.Read(ref _destinationAnalysisGeneration))
            {
                DestinationInfoText = text;
            }
        });
    }

    private CancellationTokenSource ReplaceOperationCancellation()
    {
        var previous = _operationCancellation;
        var next = new CancellationTokenSource();
        _operationCancellation = next;
        previous?.Dispose();
        return next;
    }

    private void ClearOperationCancellation(CancellationTokenSource cts)
    {
        if (ReferenceEquals(_operationCancellation, cts))
        {
            _operationCancellation = null;
        }

        cts.Dispose();
    }

    private static CancellationTokenSource ReplaceAnalysisCancellation(ref CancellationTokenSource? field)
    {
        var previous = field;
        var next = new CancellationTokenSource();
        field = next;
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        previous?.Dispose();
        return next;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _orchestrator.TransferChanged -= OnTransferChanged;
        _orchestrator.TransferRemoved -= OnTransferRemoved;
        if (_volumeChangeMonitor is not null)
        {
            _volumeChangeMonitor.VolumesChanged -= OnVolumesChanged;
        }
        if (_orchestrator is IDisposable disposableOrchestrator)
        {
            disposableOrchestrator.Dispose();
        }
        CancelAndDispose(ref _operationCancellation);
        CancelAndDispose(ref _sourceAnalysisCancellation);
        CancelAndDispose(ref _destinationAnalysisCancellation);
        Interlocked.Increment(ref _sourceAnalysisGeneration);
        Interlocked.Increment(ref _destinationAnalysisGeneration);
    }

    private static void CancelAndDispose(ref CancellationTokenSource? field)
    {
        var source = field;
        field = null;
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        source?.Dispose();
    }

    private static string FormatSourceAnalysis(PathAnalysis analysis)
    {
        if (!string.IsNullOrWhiteSpace(analysis.ErrorMessage))
        {
            return analysis.ErrorMessage;
        }

        if (!analysis.VolumeReady)
        {
            return "Volume is not ready.";
        }

        if (!analysis.Exists)
        {
            return "Source file not found.";
        }

        return FormatSingleBytes(analysis.SizeBytes);
    }

    private static string FormatDestinationAnalysis(PathAnalysis analysis)
    {
        if (!string.IsNullOrWhiteSpace(analysis.ErrorMessage))
        {
            return analysis.ErrorMessage;
        }

        if (!analysis.VolumeReady)
        {
            return "Volume is not ready.";
        }

        if (analysis.AvailableFreeSpace is long freeSpace)
        {
            var existing = analysis.Exists ? "Destination file exists. " : string.Empty;
            return $"{existing}Free space: {FormatSingleBytes(freeSpace)}";
        }

        return analysis.Exists ? "Destination file exists." : "Destination path is ready.";
    }

    private static string FormatBytes(long copied, long total) =>
        total <= 0 ? FormatSingleBytes(copied) : $"{FormatSingleBytes(copied)} / {FormatSingleBytes(total)}";

    private static string FormatSingleBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static string FormatSpeed(double bytesPerSecond) =>
        bytesPerSecond <= 0d ? "—" : $"{FormatSingleBytes((long)bytesPerSecond)}/s";

    private static string FormatEta(TimeSpan? eta) =>
        eta is null ? "—" : eta.Value.ToString(@"hh\:mm\:ss");
}
