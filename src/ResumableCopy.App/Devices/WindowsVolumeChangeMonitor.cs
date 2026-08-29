using System.IO;
using System.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResumableCopy.Application.Abstractions;
using ResumableCopy.Core.Devices;

namespace ResumableCopy.App.Devices;

public sealed class WindowsVolumeChangeMonitor : IVolumeChangeMonitor
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly ILogger<WindowsVolumeChangeMonitor> _logger;
    private readonly DriveProvider _driveProvider = new();
    private ManagementEventWatcher? _watcher;
    private Timer? _pollTimer;
    private string _lastDriveSnapshot = string.Empty;
    private bool _disposed;

    public WindowsVolumeChangeMonitor(ILogger<WindowsVolumeChangeMonitor>? logger = null)
    {
        _logger = logger ?? NullLogger<WindowsVolumeChangeMonitor>.Instance;
    }

    public event EventHandler? VolumesChanged;

    public void Start()
    {
        if (_pollTimer is not null)
        {
            return;
        }

        TryStartWmiWatcher();
        _lastDriveSnapshot = CaptureDriveSnapshot();
        _pollTimer = new Timer(OnPollTick, null, PollInterval, PollInterval);
        _logger.LogInformation("Windows volume change monitor started (WMI + polling fallback).");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_pollTimer is not null)
        {
            _pollTimer.Dispose();
            _pollTimer = null;
        }

        if (_watcher is null)
        {
            return;
        }

        try
        {
            _watcher.EventArrived -= OnVolumeEventArrived;
            _watcher.Stop();
            _watcher.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Error while stopping Windows volume change monitor.");
        }
    }

    private void TryStartWmiWatcher()
    {
        if (_watcher is not null)
        {
            return;
        }

        try
        {
            // 1 = configuration changed, 2 = device arrival, 3 = device removal
            var query = new WqlEventQuery(
                "SELECT * FROM Win32_VolumeChangeEvent WHERE EventType = 1 OR EventType = 2 OR EventType = 3");
            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += OnVolumeEventArrived;
            _watcher.Start();
            _logger.LogDebug("WMI volume change watcher started.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to start WMI volume change watcher. Polling fallback remains active.");
        }
    }

    private void OnVolumeEventArrived(object sender, EventArrivedEventArgs e)
    {
        _logger.LogDebug("Detected WMI volume change event.");
        NotifyVolumesChanged();
    }

    private void OnPollTick(object? state)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var snapshot = CaptureDriveSnapshot();
            if (string.Equals(snapshot, _lastDriveSnapshot, StringComparison.Ordinal))
            {
                return;
            }

            _lastDriveSnapshot = snapshot;
            _logger.LogDebug("Detected drive state change via polling.");
            NotifyVolumesChanged();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Drive polling tick failed.");
        }
    }

    private string CaptureDriveSnapshot()
    {
        var parts = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Removable or DriveType.Fixed or DriveType.Network))
            {
                continue;
            }

            var ready = _driveProvider.IsVolumeReady(drive.Name);
            parts.Add($"{drive.Name}:{drive.DriveType}:{ready}");
        }

        parts.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join("|", parts);
    }

    private void NotifyVolumesChanged()
    {
        _lastDriveSnapshot = CaptureDriveSnapshot();
        VolumesChanged?.Invoke(this, EventArgs.Empty);
    }
}
