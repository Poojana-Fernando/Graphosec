using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Configuration;
using ResumableCopy.Core.Devices;

namespace ResumableCopy.Core.Tests;

public class CachedDriveProviderTests
{
    [Fact]
    public void IsVolumeReady_CachesResultWithinTtl()
    {
        var inner = new CountingDriveProvider { Ready = true };
        var cached = new CachedDriveProvider(inner, new DeviceProbeOptions
        {
            ReadinessCacheTtl = TimeSpan.FromSeconds(30)
        });

        Assert.True(cached.IsVolumeReady(@"C:\one.bin"));
        Assert.True(cached.IsVolumeReady(@"C:\two.bin"));
        Assert.Equal(1, inner.ReadyCalls);
    }

    [Fact]
    public void IsVolumeReady_WithZeroTtl_DoesNotCache()
    {
        var inner = new CountingDriveProvider { Ready = true };
        var cached = new CachedDriveProvider(inner, new DeviceProbeOptions
        {
            ReadinessCacheTtl = TimeSpan.Zero
        });

        Assert.True(cached.IsVolumeReady(@"C:\one.bin"));
        Assert.True(cached.IsVolumeReady(@"C:\one.bin"));
        Assert.Equal(2, inner.ReadyCalls);
    }

    [Fact]
    public void IsVolumeReady_CachesPerVolumeRoot()
    {
        var inner = new CountingDriveProvider { Ready = true };
        var cached = new CachedDriveProvider(inner, new DeviceProbeOptions
        {
            ReadinessCacheTtl = TimeSpan.FromSeconds(30)
        });

        Assert.True(cached.IsVolumeReady(@"C:\one.bin"));
        Assert.True(cached.IsVolumeReady(@"D:\two.bin"));
        Assert.Equal(2, inner.ReadyCalls);
    }

    [Fact]
    public async Task IsVolumeReady_RequeriesAfterTtlExpires()
    {
        var inner = new CountingDriveProvider { Ready = true };
        var cached = new CachedDriveProvider(inner, new DeviceProbeOptions
        {
            ReadinessCacheTtl = TimeSpan.FromMilliseconds(30)
        });

        Assert.True(cached.IsVolumeReady(@"C:\one.bin"));
        await Task.Delay(50);
        inner.Ready = false;
        Assert.False(cached.IsVolumeReady(@"C:\one.bin"));
        Assert.Equal(2, inner.ReadyCalls);
    }

    [Fact]
    public void InvalidateReadinessCache_ForcesImmediateRequery()
    {
        var inner = new CountingDriveProvider { Ready = true };
        var cached = new CachedDriveProvider(inner, new DeviceProbeOptions
        {
            ReadinessCacheTtl = TimeSpan.FromSeconds(30)
        });

        Assert.True(cached.IsVolumeReady(@"C:\one.bin"));
        inner.Ready = false;
        cached.InvalidateReadinessCache(@"C:\one.bin");
        Assert.False(cached.IsVolumeReady(@"C:\one.bin"));
        Assert.Equal(2, inner.ReadyCalls);
    }

    [Fact]
    public async Task IsVolumeReady_NotReadyResultExpiresFasterThanReadyResult()
    {
        var inner = new CountingDriveProvider { Ready = false };
        var cached = new CachedDriveProvider(inner, new DeviceProbeOptions
        {
            ReadinessCacheTtl = TimeSpan.FromSeconds(30),
            NotReadyReadinessCacheTtl = TimeSpan.FromMilliseconds(30)
        });

        Assert.False(cached.IsVolumeReady(@"C:\one.bin"));
        inner.Ready = true;
        await Task.Delay(50);
        Assert.True(cached.IsVolumeReady(@"C:\one.bin"));
        Assert.Equal(2, inner.ReadyCalls);
    }

    private sealed class CountingDriveProvider : IDriveProvider
    {
        public bool Ready { get; set; } = true;

        public int ReadyCalls { get; private set; }

        public string? GetVolumeRoot(string path) => Path.GetPathRoot(Path.GetFullPath(path));

        public bool IsVolumeReady(string path)
        {
            ReadyCalls++;
            return Ready;
        }

        public void InvalidateReadinessCache(string? path = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ReadyCalls = 0;
            }
        }
    }
}
