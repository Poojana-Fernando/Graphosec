using System.Collections.Concurrent;
using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Configuration;

namespace ResumableCopy.Core.Devices;

public sealed class CachedDriveProvider : IDriveProvider
{
    private static readonly TimeSpan DefaultNegativeCacheTtl = TimeSpan.FromMilliseconds(400);

    private readonly IDriveProvider _inner;
    private readonly TimeSpan _readyCacheTtl;
    private readonly TimeSpan _notReadyCacheTtl;
    private readonly ConcurrentDictionary<string, CacheEntry> _readinessCache = new(StringComparer.OrdinalIgnoreCase);

    public CachedDriveProvider(IDriveProvider inner, DeviceProbeOptions? options = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _readyCacheTtl = options?.ReadinessCacheTtl ?? DeviceProbeOptions.DefaultReadinessCacheTtl;
        if (_readyCacheTtl < TimeSpan.Zero)
        {
            _readyCacheTtl = TimeSpan.Zero;
        }

        _notReadyCacheTtl = options?.NotReadyReadinessCacheTtl ?? DefaultNegativeCacheTtl;
        if (_notReadyCacheTtl < TimeSpan.Zero)
        {
            _notReadyCacheTtl = TimeSpan.Zero;
        }
    }

    public string? GetVolumeRoot(string path) => _inner.GetVolumeRoot(path);

    public bool IsVolumeReady(string path)
    {
        var root = _inner.GetVolumeRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        if (TryGetCachedReadiness(root, out var cachedReady))
        {
            return cachedReady;
        }

        var isReady = _inner.IsVolumeReady(path);
        CacheReadiness(root, isReady);
        return isReady;
    }

    public void InvalidateReadinessCache(string? path = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _readinessCache.Clear();
            return;
        }

        var root = _inner.GetVolumeRoot(path);
        if (!string.IsNullOrWhiteSpace(root))
        {
            _readinessCache.TryRemove(root, out _);
        }
    }

    private bool TryGetCachedReadiness(string root, out bool isReady)
    {
        isReady = false;
        if (_readyCacheTtl <= TimeSpan.Zero && _notReadyCacheTtl <= TimeSpan.Zero)
        {
            return false;
        }

        if (!_readinessCache.TryGetValue(root, out var cached) || cached.ExpiresUtc <= DateTime.UtcNow)
        {
            return false;
        }

        isReady = cached.IsReady;
        return true;
    }

    private void CacheReadiness(string root, bool isReady)
    {
        var ttl = isReady ? _readyCacheTtl : _notReadyCacheTtl;
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        _readinessCache[root] = new CacheEntry(isReady, DateTime.UtcNow + ttl);
    }

    private readonly record struct CacheEntry(bool IsReady, DateTime ExpiresUtc);
}
