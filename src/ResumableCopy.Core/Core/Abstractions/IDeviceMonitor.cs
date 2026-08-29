namespace ResumableCopy.Core.Abstractions;

public interface IDeviceMonitor
{
    bool IsPathAccessible(string path);

    bool IsVolumeReady(string path);

    string? GetVolumeRoot(string path);
}
