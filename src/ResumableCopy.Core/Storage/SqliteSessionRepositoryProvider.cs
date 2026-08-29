using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Storage.Sqlite;

namespace ResumableCopy.Core.Storage;

public sealed class SqliteSessionRepositoryProvider : ISessionRepositoryProvider
{
    private readonly IStagingLocator _stagingLocator;
    private readonly SqliteSessionRepositoryOptions _options;

    public SqliteSessionRepositoryProvider(
        IStagingLocator stagingLocator,
        SqliteSessionRepositoryOptions? options = null)
    {
        _stagingLocator = stagingLocator ?? throw new ArgumentNullException(nameof(stagingLocator));
        _options = options ?? new SqliteSessionRepositoryOptions();
    }

    public ISessionRepository GetRepository(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var cacheDirectory = _stagingLocator.GetCacheDirectory(destinationPath);
        return new SqliteSessionRepository(cacheDirectory, _options);
    }
}
