namespace ResumableCopy.Application.Models;

public sealed class DriveInfoSnapshot
{
    public required string RootPath { get; init; }

    public string? VolumeLabel { get; init; }

    public string DriveType { get; init; } = string.Empty;

    public bool IsRemovable { get; init; }

    public bool IsReady { get; init; }

    public long? TotalBytes { get; init; }

    public long? FreeBytes { get; init; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(VolumeLabel)
            ? RootPath.TrimEnd('\\')
            : $"{RootPath.TrimEnd('\\')} ({VolumeLabel})";
}
