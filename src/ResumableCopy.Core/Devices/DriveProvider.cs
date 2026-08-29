using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Core.Devices;

public sealed class DriveProvider : IDriveProvider
{
    public string? GetVolumeRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        return Path.GetPathRoot(fullPath);
    }

    public bool IsVolumeReady(string path) => ProbeVolumeReady(GetVolumeRoot(path));

    public void InvalidateReadinessCache(string? path = null)
    {
    }

    internal static bool ProbeVolumeReady(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            var drive = new DriveInfo(root);
            var isRemovable = drive.DriveType is DriveType.Removable or DriveType.Unknown;

            if (!drive.IsReady && !isRemovable)
            {
                return false;
            }

            if (!Directory.Exists(NormalizeRootPath(root)))
            {
                return false;
            }

            // Forces the OS to verify the volume is actually mounted (catches ghost USB letters).
            if (drive.IsReady)
            {
                _ = drive.AvailableFreeSpace;
            }

            if (isRemovable)
            {
                return ProbeRemovableVolumeAccess(root);
            }

            return true;
        }
        catch (DriveNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool ProbeRemovableVolumeAccess(string root)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(root).FirstOrDefault();
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string NormalizeRootPath(string root) =>
        root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
