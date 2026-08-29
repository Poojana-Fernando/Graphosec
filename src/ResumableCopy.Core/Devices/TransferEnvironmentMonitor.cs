using System.Collections.Concurrent;
using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Configuration;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;

namespace ResumableCopy.Core.Devices;

public sealed class TransferEnvironmentMonitor : ITransferEnvironmentMonitor
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IDeviceMonitor _deviceMonitor;
    private readonly IDiskSpaceManager _diskSpaceManager;
    private readonly ISourceIdentityProvider _sourceIdentityProvider;
    private readonly IStagingLocator _stagingLocator;
    private readonly TimeSpan _identityCacheTtl;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _identityCheckedAt = new(StringComparer.Ordinal);

    public TransferEnvironmentMonitor(
        IFileSystemService fileSystemService,
        IDeviceMonitor deviceMonitor,
        IDiskSpaceManager diskSpaceManager,
        ISourceIdentityProvider sourceIdentityProvider,
        IStagingLocator stagingLocator,
        DeviceProbeOptions? probeOptions = null)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _deviceMonitor = deviceMonitor ?? throw new ArgumentNullException(nameof(deviceMonitor));
        _diskSpaceManager = diskSpaceManager ?? throw new ArgumentNullException(nameof(diskSpaceManager));
        _sourceIdentityProvider = sourceIdentityProvider ?? throw new ArgumentNullException(nameof(sourceIdentityProvider));
        _stagingLocator = stagingLocator ?? throw new ArgumentNullException(nameof(stagingLocator));
        _identityCacheTtl = probeOptions?.ReadinessCacheTtl ?? TimeSpan.Zero;
        if (_identityCacheTtl < TimeSpan.Zero)
        {
            _identityCacheTtl = TimeSpan.Zero;
        }
    }

    public void EnsureReadyToStart(string sourcePath, string destinationPath, long totalBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        EnsureSourceAccessible(sourcePath);
        EnsureDestinationAccessible(destinationPath);
        _diskSpaceManager.EnsureSufficientSpace(destinationPath, totalBytes);
    }

    public void EnsureReadyForChunk(CopySession session, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        EnsureSourceAccessible(session.SourcePath);

        var cacheDirectory = _stagingLocator.GetCacheDirectory(destinationPath);
        EnsureDestinationAccessible(cacheDirectory);

        var remainingBytes = session.SourceIdentity.Length - session.CompletedBytes;
        if (remainingBytes > 0)
        {
            _diskSpaceManager.EnsureRemainingSpace(cacheDirectory, remainingBytes);
        }

        EnsureSourceIdentityUnchanged(session);
    }

    public void EnsureSourceIdentityUnchanged(CopySession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (_identityCacheTtl > TimeSpan.Zero
            && _identityCheckedAt.TryGetValue(session.SessionId, out var lastCheck)
            && DateTimeOffset.UtcNow - lastCheck < _identityCacheTtl)
        {
            return;
        }

        if (!_fileSystemService.FileExists(session.SourcePath))
        {
            throw new SourceUnavailableException($"Source file not found: '{session.SourcePath}'.");
        }

        var currentIdentity = _sourceIdentityProvider.Capture(session.SourcePath);
        if (!currentIdentity.Matches(session.SourceIdentity))
        {
            throw new SourceChangedException("Source file changed during transfer.");
        }

        if (_identityCacheTtl > TimeSpan.Zero)
        {
            _identityCheckedAt[session.SessionId] = DateTimeOffset.UtcNow;
        }
    }

    private void EnsureSourceAccessible(string sourcePath)
    {
        if (!_deviceMonitor.IsVolumeReady(sourcePath))
        {
            throw new SourceUnavailableException($"Source volume is not ready for '{sourcePath}'.");
        }

        if (!_fileSystemService.FileExists(sourcePath))
        {
            throw new SourceUnavailableException($"Source file not found: '{sourcePath}'.");
        }

        if (!_deviceMonitor.IsPathAccessible(sourcePath))
        {
            throw new SourceUnavailableException($"Source path is not accessible: '{sourcePath}'.");
        }
    }

    private void EnsureDestinationAccessible(string destinationPath)
    {
        if (!_deviceMonitor.IsVolumeReady(destinationPath))
        {
            throw new DestinationUnavailableException($"Destination volume is not ready for '{destinationPath}'.");
        }

        var fullPath = Path.GetFullPath(destinationPath);

        if (Directory.Exists(fullPath))
        {
            if (!_deviceMonitor.IsPathAccessible(fullPath))
            {
                throw new DestinationUnavailableException($"Destination path is not accessible: '{destinationPath}'.");
            }

            return;
        }

        if (_fileSystemService.FileExists(fullPath))
        {
            if (!_deviceMonitor.IsPathAccessible(fullPath))
            {
                throw new DestinationUnavailableException($"Destination path is not accessible: '{destinationPath}'.");
            }

            return;
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            && !_deviceMonitor.IsPathAccessible(directory))
        {
            throw new DestinationUnavailableException($"Destination path is not accessible: '{destinationPath}'.");
        }
    }
}
