using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Errors;

namespace ResumableCopy.Core.Security;

public sealed class NullReparsePointInspector : IReparsePointInspector
{
    public static NullReparsePointInspector Instance { get; } = new();

    private NullReparsePointInspector()
    {
    }

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
    }

    public bool IsReparsePoint(string path) => false;
}
