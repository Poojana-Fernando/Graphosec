using ResumableCopy.Core.Errors;

namespace ResumableCopy.Core.Security;

internal static class PathNormalization
{
    public static string NormalizeAbsolutePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Path.IsPathRooted(path))
        {
            throw new InvalidPathException($"Path must be absolute: '{path}'.");
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new InvalidPathException("Path cannot be empty.");
        }

        ValidateUncPath(trimmed);
        ValidatePathSegments(trimmed);

        return Path.GetFullPath(trimmed);
    }

    public static string ExpandForIo(string normalizedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);

        if (!OperatingSystem.IsWindows() || normalizedPath.Length < 260)
        {
            return normalizedPath;
        }

        if (normalizedPath.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return normalizedPath;
        }

        if (normalizedPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + normalizedPath[2..];
        }

        return @"\\?\" + normalizedPath;
    }

    public static string TrimTrailingSeparators(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(trimmed) ? path : trimmed;
    }

    public static bool PathsOverlap(string pathA, string pathB)
    {
        var normalizedA = TrimTrailingSeparators(pathA);
        var normalizedB = TrimTrailingSeparators(pathB);

        if (string.Equals(normalizedA, normalizedB, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var separator = Path.DirectorySeparatorChar;
        var prefixA = normalizedA + separator;
        var prefixB = normalizedB + separator;

        return normalizedA.StartsWith(prefixB, StringComparison.OrdinalIgnoreCase)
            || normalizedB.StartsWith(prefixA, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateUncPath(string path)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return;
        }

        var segments = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            throw new InvalidPathException($"UNC path must include server and share: '{path}'.");
        }
    }

    private static void ValidatePathSegments(string path)
    {
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new InvalidPathException($"Path contains invalid characters: '{path}'.");
        }

        var fileName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(fileName) && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidPathException($"Path contains invalid characters: '{path}'.");
        }
    }
}
