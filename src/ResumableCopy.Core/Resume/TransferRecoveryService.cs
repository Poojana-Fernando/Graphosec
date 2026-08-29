using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Storage;

namespace ResumableCopy.Core.Resume;

public sealed class TransferRecoveryService : ITransferRecoveryService
{
    private readonly ISessionRepositoryProvider _sessionRepositoryProvider;
    private readonly IFileSystemService _fileSystemService;
    private readonly ISourceIdentityProvider _sourceIdentityProvider;
    private readonly IStagingChunkValidator _stagingChunkValidator;
    private readonly IDeviceMonitor _deviceMonitor;
    private readonly IStagingLocator _stagingLocator;
    private readonly ILogger<TransferRecoveryService> _logger;

    public TransferRecoveryService(
        ISessionRepositoryProvider sessionRepositoryProvider,
        IFileSystemService fileSystemService,
        ISourceIdentityProvider sourceIdentityProvider,
        IStagingChunkValidator stagingChunkValidator,
        IDeviceMonitor deviceMonitor,
        IStagingLocator stagingLocator,
        ILogger<TransferRecoveryService>? logger = null)
    {
        _sessionRepositoryProvider = sessionRepositoryProvider ?? throw new ArgumentNullException(nameof(sessionRepositoryProvider));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _sourceIdentityProvider = sourceIdentityProvider ?? throw new ArgumentNullException(nameof(sourceIdentityProvider));
        _stagingChunkValidator = stagingChunkValidator ?? throw new ArgumentNullException(nameof(stagingChunkValidator));
        _deviceMonitor = deviceMonitor ?? throw new ArgumentNullException(nameof(deviceMonitor));
        _stagingLocator = stagingLocator ?? throw new ArgumentNullException(nameof(stagingLocator));
        _logger = logger ?? NullLogger<TransferRecoveryService>.Instance;
    }

    public async ValueTask<IReadOnlyList<RecoverableSessionInfo>> DiscoverRecoverableSessionsAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (!_deviceMonitor.IsVolumeReady(destinationPath))
        {
            throw new DestinationUnavailableException(
                $"Destination volume is not ready for '{destinationPath}'. Reconnect the drive and try again.");
        }

        var cacheDirectory = _stagingLocator.GetCacheDirectory(destinationPath);
        if (!_deviceMonitor.IsVolumeReady(cacheDirectory))
        {
            throw new DestinationUnavailableException(
                "Destination storage is not available. Reconnect the drive and try again.");
        }

        var repository = _sessionRepositoryProvider.GetRepository(destinationPath);
        try
        {
            var sessions = await repository.FindUnfinishedAsync(cancellationToken).ConfigureAwait(false);
            return sessions.Select(ToRecoverableSessionInfo).ToArray();
        }
        finally
        {
            DisposeIfNeeded(repository);
        }
    }

    public async ValueTask<RecoveryResult> RecoverSessionAsync(
        string destinationPath,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var repository = _sessionRepositoryProvider.GetRepository(destinationPath);
        try
        {
            var session = await repository.FindAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                return new RecoveryResult(
                    sessionId,
                    CopyState.Failed,
                    CanResume: false,
                    InvalidatedChunkCount: 0,
                    Message: $"Session '{sessionId}' was not found.");
            }

            return await RecoverSessionInternalAsync(session, repository, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DisposeIfNeeded(repository);
        }
    }

    public async ValueTask<IReadOnlyList<RecoveryResult>> RecoverUnfinishedSessionsAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var repository = _sessionRepositoryProvider.GetRepository(destinationPath);
        try
        {
            var sessions = await repository.FindUnfinishedAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<RecoveryResult>(sessions.Count);

            foreach (var session in sessions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await RecoverSessionInternalAsync(session, repository, cancellationToken).ConfigureAwait(false));
            }

            return results;
        }
        finally
        {
            DisposeIfNeeded(repository);
        }
    }

    private async Task<RecoveryResult> RecoverSessionInternalAsync(
        CopySession session,
        ISessionRepository repository,
        CancellationToken cancellationToken)
    {
        if (session.State is CopyState.Completed or CopyState.Cancelled)
        {
            return new RecoveryResult(
                session.SessionId,
                session.State,
                CanResume: false,
                InvalidatedChunkCount: 0,
                Message: "Session is already finished.",
                session);
        }

        if (!_fileSystemService.FileExists(session.SourcePath))
        {
            session.State = CopyState.WaitingForSource;
            session.LastError = "Source file is no longer available.";
            await repository.SaveAsync(session, cancellationToken).ConfigureAwait(false);

            return new RecoveryResult(
                session.SessionId,
                session.State,
                CanResume: false,
                InvalidatedChunkCount: 0,
                Message: session.LastError,
                session);
        }

        if (!_deviceMonitor.IsVolumeReady(session.SourcePath))
        {
            session.State = CopyState.WaitingForSource;
            session.LastError = "Source volume is not ready.";
            await repository.SaveAsync(session, cancellationToken).ConfigureAwait(false);

            return new RecoveryResult(
                session.SessionId,
                session.State,
                CanResume: false,
                InvalidatedChunkCount: 0,
                Message: session.LastError,
                session);
        }

        var cacheDirectory = _stagingLocator.GetCacheDirectory(session.DestinationPath);
        if (!_deviceMonitor.IsVolumeReady(cacheDirectory)
            || (Directory.Exists(cacheDirectory) && !_deviceMonitor.IsPathAccessible(cacheDirectory)))
        {
            session.State = CopyState.WaitingForDestination;
            session.LastError = "Destination volume is not ready.";
            await repository.SaveAsync(session, cancellationToken).ConfigureAwait(false);

            return new RecoveryResult(
                session.SessionId,
                session.State,
                CanResume: false,
                InvalidatedChunkCount: 0,
                Message: session.LastError,
                session);
        }

        SourceIdentity currentIdentity;
        try
        {
            currentIdentity = _sourceIdentityProvider.Capture(session.SourcePath);
        }
        catch (Exception exception)
        {
            session.State = CopyState.WaitingForSource;
            session.LastError = exception.Message;
            await repository.SaveAsync(session, cancellationToken).ConfigureAwait(false);

            return new RecoveryResult(
                session.SessionId,
                session.State,
                CanResume: false,
                InvalidatedChunkCount: 0,
                Message: session.LastError,
                session);
        }

        if (!currentIdentity.Matches(session.SourceIdentity))
        {
            session.State = CopyState.RecoveryRequired;
            session.LastError = "Source file changed since the transfer was interrupted.";
            await repository.SaveAsync(session, cancellationToken).ConfigureAwait(false);

            return new RecoveryResult(
                session.SessionId,
                session.State,
                CanResume: false,
                InvalidatedChunkCount: 0,
                Message: session.LastError,
                session);
        }

        var invalidatedChunks = 0;
        var stagingExists = _fileSystemService.FileExists(session.StagingPath);

        foreach (var chunk in session.Chunks.Where(static chunk => chunk.IsComplete))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isValid = stagingExists
                && await _stagingChunkValidator.ValidateChunkAsync(session.StagingPath, chunk, cancellationToken)
                    .ConfigureAwait(false);

            if (isValid)
            {
                continue;
            }

            chunk.IsComplete = false;
            chunk.Hash = null;
            await repository.MarkChunkPendingAsync(session.SessionId, chunk, cancellationToken).ConfigureAwait(false);
            invalidatedChunks++;
        }

        if (invalidatedChunks > 0)
        {
            session.LastError = $"{invalidatedChunks} chunk(s) failed recovery validation and were reset to pending.";
        }
        else
        {
            session.LastError = null;
        }

        session.State = CopyState.Paused;
        await repository.SaveAsync(session, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Recovered session {SessionId}. CanResume={CanResume}, InvalidatedChunks={InvalidatedChunks}",
            session.SessionId,
            true,
            invalidatedChunks);

        return new RecoveryResult(
            session.SessionId,
            session.State,
            CanResume: true,
            InvalidatedChunkCount: invalidatedChunks,
            Message: invalidatedChunks > 0 ? session.LastError : "Session is ready to resume.",
            session);
    }

    private static RecoverableSessionInfo ToRecoverableSessionInfo(CopySession session)
    {
        return new RecoverableSessionInfo(
            session.SessionId,
            session.SourcePath,
            session.DestinationPath,
            session.State,
            session.SourceIdentity.Length,
            session.CompletedBytes,
            session.CompletedChunkCount,
            session.TotalChunks,
            session.LastError);
    }

    private static void DisposeIfNeeded(ISessionRepository repository)
    {
        if (repository is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
