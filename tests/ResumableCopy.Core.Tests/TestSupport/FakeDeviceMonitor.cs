using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Core.Tests.TestSupport;

public sealed class FakeDeviceMonitor : IDeviceMonitor
{
    private readonly IDeviceMonitor _inner;
    private readonly HashSet<string> _unreadyVolumeRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inaccessiblePaths = new(StringComparer.OrdinalIgnoreCase);

    public FakeDeviceMonitor(IDeviceMonitor inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void SetVolumeNotReady(string path)
    {
        var root = _inner.GetVolumeRoot(path);
        if (!string.IsNullOrWhiteSpace(root))
        {
            _unreadyVolumeRoots.Add(root);
        }
    }

    public void SetPathInaccessible(string path) =>
        _inaccessiblePaths.Add(Path.GetFullPath(path));

    public void Reset()
    {
        _unreadyVolumeRoots.Clear();
        _inaccessiblePaths.Clear();
    }

    public bool IsPathAccessible(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (_inaccessiblePaths.Contains(fullPath))
        {
            return false;
        }

        var root = _inner.GetVolumeRoot(path);
        if (!string.IsNullOrWhiteSpace(root) && _unreadyVolumeRoots.Contains(root))
        {
            return false;
        }

        return _inner.IsPathAccessible(path);
    }

    public bool IsVolumeReady(string path)
    {
        var root = _inner.GetVolumeRoot(path);
        if (!string.IsNullOrWhiteSpace(root) && _unreadyVolumeRoots.Contains(root))
        {
            return false;
        }

        return _inner.IsVolumeReady(path);
    }

    public string? GetVolumeRoot(string path) => _inner.GetVolumeRoot(path);
}
