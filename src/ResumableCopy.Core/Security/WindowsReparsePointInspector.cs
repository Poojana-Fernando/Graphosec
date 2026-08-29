using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Errors;

namespace ResumableCopy.Core.Security;

public sealed class WindowsReparsePointInspector : IReparsePointInspector
{
    public void EnsureRegularFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (Directory.Exists(path))
        {
            throw new PermissionDeniedException($"Path is a directory, not a file: '{path}'.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File not found: '{path}'.", path);
        }

        if (IsReparsePoint(path))
        {
            throw new PermissionDeniedException(
                $"Reparse points (symlinks, junctions, mount points) are not supported: '{path}'.");
        }
    }

    public bool IsReparsePoint(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint);
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
}
