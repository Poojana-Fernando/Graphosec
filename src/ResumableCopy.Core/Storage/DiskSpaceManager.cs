using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Configuration;
using ResumableCopy.Core.Errors;

namespace ResumableCopy.Core.Storage;

public sealed class DiskSpaceManager : IDiskSpaceManager
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IDriveProvider? _driveProvider;
    private readonly TimeSpan _cacheTtl;
    private readonly object _cacheSync = new();
    private readonly Dictionary<string, CacheEntry> _freeSpaceCache = new(StringComparer.OrdinalIgnoreCase);

    public DiskSpaceManager(
        IFileSystemService fileSystemService,
        IDriveProvider? driveProvider = null,
        DeviceProbeOptions? probeOptions = null)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _driveProvider = driveProvider;
        _cacheTtl = probeOptions?.ReadinessCacheTtl ?? TimeSpan.Zero;
        if (_cacheTtl < TimeSpan.Zero)
        {
            _cacheTtl = TimeSpan.Zero;
        }
    }

    public void EnsureRemainingSpace(string destinationPath, long remainingBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (remainingBytes <= 0)
        {
            return;
        }

        EnsureSufficientSpace(destinationPath, remainingBytes);
    }

    public bool HasRemainingSpace(string destinationPath, long remainingBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (remainingBytes <= 0)
        {
            return true;
        }

        return GetAvailableFreeSpace(destinationPath) >= remainingBytes;
    }

    public void EnsureSufficientSpace(string destinationPath, long requiredBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var available = GetAvailableFreeSpace(destinationPath);
        if (available < requiredBytes)
        {
            throw new InsufficientStorageException(
                $"Insufficient storage at '{destinationPath}'. Required {requiredBytes} bytes, available {available} bytes.");
        }
    }

    public long GetAvailableFreeSpace(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (_driveProvider is not null && !_driveProvider.IsVolumeReady(destinationPath))
        {
            throw new DestinationUnavailableException($"Destination volume is not ready for '{destinationPath}'.");
        }

        var root = _driveProvider?.GetVolumeRoot(destinationPath);
        if (_cacheTtl > TimeSpan.Zero && !string.IsNullOrWhiteSpace(root))
        {
            lock (_cacheSync)
            {
                if (_freeSpaceCache.TryGetValue(root, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
                {
                    return cached.Bytes;
                }
            }
        }

        long available;
        try
        {
            available = _fileSystemService.GetAvailableFreeSpace(destinationPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DriveNotFoundException)
        {
            throw new DestinationUnavailableException($"Unable to query free space at '{destinationPath}'.", exception);
        }

        if (_cacheTtl > TimeSpan.Zero && !string.IsNullOrWhiteSpace(root))
        {
            lock (_cacheSync)
            {
                _freeSpaceCache[root] = new CacheEntry(available, DateTime.UtcNow + _cacheTtl);
            }
        }

        return available;
    }

    public void InvalidateCache(string? destinationPath = null)
    {
        lock (_cacheSync)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                _freeSpaceCache.Clear();
                return;
            }

            var root = _driveProvider?.GetVolumeRoot(destinationPath);
            if (!string.IsNullOrWhiteSpace(root))
            {
                _freeSpaceCache.Remove(root);
            }
        }
    }

    private readonly record struct CacheEntry(long Bytes, DateTime ExpiresUtc);
}
