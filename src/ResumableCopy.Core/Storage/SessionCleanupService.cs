using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Core.Storage;

public sealed class SessionCleanupService : ISessionCleanupService
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IStagingLocator _stagingLocator;
    private readonly ISessionRepositoryProvider _sessionRepositoryProvider;
    private readonly ILogger<SessionCleanupService> _logger;

    public SessionCleanupService(
        IFileSystemService fileSystemService,
        IStagingLocator stagingLocator,
        ISessionRepositoryProvider sessionRepositoryProvider,
        ILogger<SessionCleanupService>? logger = null)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _stagingLocator = stagingLocator ?? throw new ArgumentNullException(nameof(stagingLocator));
        _sessionRepositoryProvider = sessionRepositoryProvider ?? throw new ArgumentNullException(nameof(sessionRepositoryProvider));
        _logger = logger ?? NullLogger<SessionCleanupService>.Instance;
    }

    public async ValueTask CleanupSessionAsync(
        string destinationPath,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        try
        {
            var cacheDirectory = _stagingLocator.GetCacheDirectory(destinationPath);
            var partPath = Path.Combine(cacheDirectory, $"{sessionId}.part");
            if (_fileSystemService.FileExists(partPath))
            {
                _fileSystemService.Delete(partPath);
            }

            var repository = _sessionRepositoryProvider.GetRepository(destinationPath);
            try
            {
                await repository.DeleteAsync(sessionId, cancellationToken).ConfigureAwait(false);
                await CleanupCacheDirectoryIfEmptyAsync(cacheDirectory, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (repository is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Best-effort cleanup failed for session {SessionId} at {DestinationPath}",
                sessionId,
                destinationPath);
        }
    }

    internal async ValueTask CleanupCacheDirectoryIfEmptyAsync(
        string cacheDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(cacheDirectory))
        {
            return;
        }

        using (var repository = new SqliteSessionRepository(cacheDirectory))
        {
            var unfinished = await repository.FindUnfinishedAsync(cancellationToken).ConfigureAwait(false);
            if (unfinished.Count != 0)
            {
                return;
            }
        }

        var databasePath = Path.Combine(cacheDirectory, StagingLocator.DatabaseFileName);
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        if (Directory.Exists(cacheDirectory) && !Directory.EnumerateFileSystemEntries(cacheDirectory).Any())
        {
            _fileSystemService.Delete(cacheDirectory);
        }
    }
}
