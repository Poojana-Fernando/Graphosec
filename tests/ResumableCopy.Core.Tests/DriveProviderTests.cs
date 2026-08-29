using ResumableCopy.Core.Devices;

namespace ResumableCopy.Core.Tests;

public class DriveProviderTests
{
    [Fact]
    public void ProbeVolumeReady_ReturnsFalse_ForInvalidRoot()
    {
        Assert.False(DriveProvider.ProbeVolumeReady(null));
        Assert.False(DriveProvider.ProbeVolumeReady(string.Empty));
        Assert.False(DriveProvider.ProbeVolumeReady("   "));
    }

    [Fact]
    public void ProbeVolumeReady_ReturnsFalse_ForUnusedDriveLetter()
    {
        var unusedRoot = GetUnusedDriveRoot();
        if (unusedRoot is null)
        {
            return;
        }

        Assert.False(DriveProvider.ProbeVolumeReady(unusedRoot));
    }

    [Fact]
    public void IsVolumeReady_ReturnsFalse_ForUnusedDriveLetter()
    {
        var unusedRoot = GetUnusedDriveRoot();
        if (unusedRoot is null)
        {
            return;
        }

        var provider = new DriveProvider();
        Assert.False(provider.IsVolumeReady(Path.Combine(unusedRoot, "missing", "file.bin")));
    }

    [Fact]
    public void InvalidateReadinessCache_DoesNotThrow()
    {
        var provider = new DriveProvider();
        provider.InvalidateReadinessCache();
        provider.InvalidateReadinessCache(@"C:\temp\file.bin");
    }

    private static string? GetUnusedDriveRoot()
    {
        foreach (var letter in "QRSTUVWXYZ")
        {
            var root = $"{letter}:\\";
            if (!DriveInfo.GetDrives().Any(drive =>
                    string.Equals(drive.Name, root, StringComparison.OrdinalIgnoreCase)))
            {
                return root;
            }
        }

        return null;
    }
}
