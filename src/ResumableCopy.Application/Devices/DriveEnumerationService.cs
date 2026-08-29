using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Models;
using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Application.Devices;

public sealed class DriveEnumerationService : IDriveEnumerationService
{
    private readonly IDriveProvider _driveProvider;

    public DriveEnumerationService(IDriveProvider driveProvider)
    {
        _driveProvider = driveProvider ?? throw new ArgumentNullException(nameof(driveProvider));
    }

    public IReadOnlyList<DriveInfoSnapshot> GetAvailableDrives()
    {
        _driveProvider.InvalidateReadinessCache();

        var drives = new List<DriveInfoSnapshot>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Removable or DriveType.Fixed or DriveType.Network))
            {
                continue;
            }

            var rootPath = drive.Name;
            var isReady = _driveProvider.IsVolumeReady(rootPath);
            long? totalBytes = null;
            long? freeBytes = null;

            if (isReady)
            {
                try
                {
                    totalBytes = drive.TotalSize;
                    freeBytes = drive.AvailableFreeSpace;
                }
                catch (IOException)
                {
                    isReady = false;
                }
                catch (UnauthorizedAccessException)
                {
                    isReady = false;
                }
            }

            string? volumeLabel = null;
            try
            {
                volumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel;
            }
            catch (IOException)
            {
            }

            drives.Add(new DriveInfoSnapshot
            {
                RootPath = rootPath,
                VolumeLabel = volumeLabel,
                DriveType = FormatDriveType(drive.DriveType, drive.DriveType == DriveType.Removable),
                IsRemovable = drive.DriveType == DriveType.Removable,
                IsReady = isReady,
                TotalBytes = totalBytes,
                FreeBytes = freeBytes
            });
        }

        return drives
            .OrderByDescending(static drive => drive.IsRemovable)
            .ThenBy(static drive => drive.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FormatDriveType(DriveType driveType, bool isRemovable) =>
        isRemovable ? "USB / Removable" : driveType.ToString();
}
