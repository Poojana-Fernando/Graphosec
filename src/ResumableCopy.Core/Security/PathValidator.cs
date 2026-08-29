using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Errors;

namespace ResumableCopy.Core.Security;

public sealed class PathValidator : IPathValidator
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public (string SourcePath, string DestinationPath) ValidateCopyPaths(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var normalizedSource = PathNormalization.NormalizeAbsolutePath(sourcePath);
        var normalizedDestination = PathNormalization.NormalizeAbsolutePath(destinationPath);

        ValidateFileName(normalizedSource, "Source");
        ValidateFileName(normalizedDestination, "Destination");

        if (string.Equals(normalizedSource, normalizedDestination, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidPathException("Source and destination paths must not be the same.");
        }

        if (PathNormalization.PathsOverlap(normalizedSource, normalizedDestination))
        {
            throw new InvalidPathException(
                "Source and destination paths overlap; copying into a nested path is not allowed.");
        }

        return (normalizedSource, normalizedDestination);
    }

    private static void ValidateFileName(string path, string role)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName))
        {
            throw new InvalidPathException($"{role} path must include a file name.");
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (ReservedDeviceNames.Contains(fileName) || ReservedDeviceNames.Contains(baseName))
        {
            throw new InvalidPathException($"{role} path uses a reserved Windows device name: '{fileName}'.");
        }
    }
}
