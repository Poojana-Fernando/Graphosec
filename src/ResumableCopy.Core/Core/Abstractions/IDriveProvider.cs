namespace ResumableCopy.Core.Abstractions;

public interface IDriveProvider
{
    string? GetVolumeRoot(string path);

    bool IsVolumeReady(string path);

    void InvalidateReadinessCache(string? path = null);
}
