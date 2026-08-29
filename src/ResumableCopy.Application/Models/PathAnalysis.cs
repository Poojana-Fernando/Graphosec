namespace ResumableCopy.Application.Models;

public sealed record PathAnalysis(
    string Path,
    bool Exists,
    bool VolumeReady,
    long SizeBytes,
    long? AvailableFreeSpace,
    string? ErrorMessage)
{
    public static PathAnalysis Empty(string path) =>
        new(path, Exists: false, VolumeReady: false, SizeBytes: 0, AvailableFreeSpace: null, ErrorMessage: null);

    public static PathAnalysis Failed(string path, string errorMessage) =>
        new(path, Exists: false, VolumeReady: false, SizeBytes: 0, AvailableFreeSpace: null, errorMessage);
}
