using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Configuration;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Storage;

public sealed class StagingLocator : IStagingLocator
{
    public const string DefaultCacheDirectoryName = ".copycache";

    public const string DatabaseFileName = "sessions.db";

    private readonly string _cacheDirectoryName;

    public StagingLocator(StagingOptions? options = null)
    {
        _cacheDirectoryName = options?.CacheDirectoryName ?? DefaultCacheDirectoryName;
    }

    public string GetCacheDirectory(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullPath = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(destinationDirectory))
        {
            destinationDirectory = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(destinationDirectory))
            {
                throw new ArgumentException("Destination path must include a directory.", nameof(destinationPath));
            }
        }

        return Path.Combine(destinationDirectory, _cacheDirectoryName);
    }

    public string GetPartFilePath(CopySession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var cacheDirectory = GetCacheDirectory(session.DestinationPath);
        return Path.Combine(cacheDirectory, $"{session.SessionId}.part");
    }

    public string GetDatabasePath(string destinationPath) =>
        Path.Combine(GetCacheDirectory(destinationPath), DatabaseFileName);
}
