using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Core.Devices;

public sealed class StorageDeviceMonitor : IDeviceMonitor
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IDriveProvider _driveProvider;

    public StorageDeviceMonitor(IFileSystemService fileSystemService, IDriveProvider driveProvider)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _driveProvider = driveProvider ?? throw new ArgumentNullException(nameof(driveProvider));
    }

    public bool IsPathAccessible(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!_driveProvider.IsVolumeReady(path))
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(directory))
            {
                return _driveProvider.IsVolumeReady(path);
            }

            return Directory.Exists(directory) || _fileSystemService.FileExists(path);
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

    public bool IsVolumeReady(string path) => _driveProvider.IsVolumeReady(path);

    public string? GetVolumeRoot(string path) => _driveProvider.GetVolumeRoot(path);
}
