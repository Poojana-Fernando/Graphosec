using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Concurrency;
using ResumableCopy.Core.Core;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Devices;
using ResumableCopy.Core.Reliability;
using ResumableCopy.Core.Resume;
using ResumableCopy.Core.Performance;
using ResumableCopy.Core.Security;
using ResumableCopy.Core.Storage;
namespace ResumableCopy.Core.Core;

public sealed class CopyEngine : ICopyEngine
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ISourceIdentityProvider _sourceIdentityProvider;
    private readonly IStagingLocator _stagingLocator;
    private readonly IHashService _hashService;
    private readonly IChunkVerifier _chunkVerifier;
    private readonly IFileVerifier _fileVerifier;
    private readonly ISessionRepositoryProvider _sessionRepositoryProvider;
    private readonly IDiskSpaceManager _diskSpaceManager;
    private readonly ITransferRecoveryService _transferRecoveryService;
    private readonly ITransferEnvironmentMonitor _environmentMonitor;
    private readonly IChunkCopyExecutor _chunkCopyExecutor;
    private readonly IFaultInjector _faultInjector;
    private readonly IPathValidator _pathValidator;
    private readonly ICopyPerformanceAdvisor _performanceAdvisor;
    private readonly ISessionCleanupService _sessionCleanupService;

    public CopyEngine(
        IFileSystemService fileSystemService,
        ISourceIdentityProvider sourceIdentityProvider,
        IStagingLocator stagingLocator,
        IHashService hashService,
        IChunkVerifier chunkVerifier,
        IFileVerifier fileVerifier,
        ISessionRepositoryProvider sessionRepositoryProvider,
        IDiskSpaceManager diskSpaceManager,
        ITransferRecoveryService transferRecoveryService,
        ITransferEnvironmentMonitor environmentMonitor,
        IChunkCopyExecutor? chunkCopyExecutor = null,
        IFaultInjector? faultInjector = null,
        IPathValidator? pathValidator = null,
        ICopyPerformanceAdvisor? performanceAdvisor = null,
        ISessionCleanupService? sessionCleanupService = null,
        ILogger<CopyEngine>? logger = null)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _sourceIdentityProvider = sourceIdentityProvider ?? throw new ArgumentNullException(nameof(sourceIdentityProvider));
        _stagingLocator = stagingLocator ?? throw new ArgumentNullException(nameof(stagingLocator));
        _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
        _chunkVerifier = chunkVerifier ?? throw new ArgumentNullException(nameof(chunkVerifier));
        _fileVerifier = fileVerifier ?? throw new ArgumentNullException(nameof(fileVerifier));
        _sessionRepositoryProvider = sessionRepositoryProvider ?? throw new ArgumentNullException(nameof(sessionRepositoryProvider));
        _diskSpaceManager = diskSpaceManager ?? throw new ArgumentNullException(nameof(diskSpaceManager));
        _transferRecoveryService = transferRecoveryService ?? throw new ArgumentNullException(nameof(transferRecoveryService));
        _environmentMonitor = environmentMonitor ?? throw new ArgumentNullException(nameof(environmentMonitor));
        _faultInjector = faultInjector ?? NullFaultInjector.Instance;
        _pathValidator = pathValidator ?? new PathValidator();
        _performanceAdvisor = performanceAdvisor ?? new CopyPerformanceAdvisor();
        _sessionCleanupService = sessionCleanupService ?? new SessionCleanupService(
            fileSystemService,
            stagingLocator,
            sessionRepositoryProvider);
        _logger = logger ?? NullLogger<CopyEngine>.Instance;
        _chunkCopyExecutor = chunkCopyExecutor ?? new ParallelChunkCopyExecutor(
            fileSystemService,
            hashService,
            chunkVerifier,
            environmentMonitor,
            _faultInjector,
            NullLogger<ParallelChunkCopyExecutor>.Instance);
    }

    private readonly ILogger<CopyEngine> _logger;

    public static CopyEngine CreateDefault()
    {
        var fileSystemService = new IO.FileSystemService();
        var hashService = new Integrity.Sha256HashService();
        var sourceIdentityProvider = new IO.SourceIdentityProvider(fileSystemService);
        var stagingLocator = new StagingLocator();
        var chunkVerifier = new Integrity.ChunkVerifier(hashService);
        var sessionRepositoryProvider = new SqliteSessionRepositoryProvider(stagingLocator);
        var driveProvider = new DriveProvider();
        var deviceMonitor = new StorageDeviceMonitor(fileSystemService, driveProvider);
        var diskSpaceManager = new DiskSpaceManager(fileSystemService, driveProvider);
        var environmentMonitor = new TransferEnvironmentMonitor(
            fileSystemService,
            deviceMonitor,
            diskSpaceManager,
            sourceIdentityProvider,
            stagingLocator);

        return new CopyEngine(
            fileSystemService,
            sourceIdentityProvider,
            stagingLocator,
            hashService,
            chunkVerifier,
            new Integrity.FileVerifier(hashService),
            sessionRepositoryProvider,
            diskSpaceManager,
            new TransferRecoveryService(
                sessionRepositoryProvider,
                fileSystemService,
                sourceIdentityProvider,
                new Integrity.StagingChunkValidator(fileSystemService, chunkVerifier),
                deviceMonitor,
                stagingLocator),
            environmentMonitor);
    }

    public async Task<CopyResult> CopyAsync(
        CopyJob job,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var stopwatch = Stopwatch.StartNew();
        var (sourcePath, destinationPath) = _pathValidator.ValidateCopyPaths(job.SourcePath, job.DestinationPath);
        var options = job.Options;

        if (!_fileSystemService.FileExists(sourcePath))
        {
            throw new SourceUnavailableException($"Source file not found: '{sourcePath}'.");
        }

        if (_fileSystemService.FileExists(destinationPath) && !options.OverwriteExisting)
        {
            throw new CopyException(CopyFailureKind.Permanent, $"Destination file already exists: '{destinationPath}'.");
        }

        var sourceIdentity = _sourceIdentityProvider.Capture(sourcePath);
        ValidateOptions(options);
        options = _performanceAdvisor.ResolveOptions(sourceIdentity.Length, options);
        ValidateOptions(options);

        var session = CreateSession(sourcePath, destinationPath, sourceIdentity, options, job.SessionId);
        var sessionRepository = _sessionRepositoryProvider.GetRepository(destinationPath);
        await sessionRepository.SaveAsync(session, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Starting copy session {SessionId} from {SourcePath} to {DestinationPath} ({TotalBytes} bytes, chunk {ChunkSize}, workers {Workers})",
            session.SessionId,
            sourcePath,
            destinationPath,
            sourceIdentity.Length,
            options.ChunkSize,
            options.MaximumWorkers);

        _environmentMonitor.EnsureReadyToStart(sourcePath, destinationPath, sourceIdentity.Length);

        return await ExecuteSessionAsync(session, options, destinationPath, stopwatch, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CopyResult> ResumeAsync(
        string sessionId,
        string destinationPath,
        CopyOptions? options,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var stopwatch = Stopwatch.StartNew();
        var normalizedDestination = PathNormalization.NormalizeAbsolutePath(destinationPath);

        var recovery = await _transferRecoveryService
            .RecoverSessionAsync(normalizedDestination, sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (!recovery.CanResume || recovery.Session is null)
        {
            throw RecoveryFailureMapper.CreateException(recovery);
        }

        var session = recovery.Session;
        var (_, validatedDestination) = _pathValidator.ValidateCopyPaths(session.SourcePath, normalizedDestination);
        normalizedDestination = validatedDestination;
        var resumeOptions = options ?? CreateOptionsFromSession(session);
        ValidateOptions(resumeOptions);
        resumeOptions = _performanceAdvisor.ResolveOptions(session.SourceIdentity.Length, resumeOptions);
        ValidateOptions(resumeOptions);

        if (!_fileSystemService.FileExists(session.SourcePath))
        {
            throw new SourceUnavailableException($"Source file not found: '{session.SourcePath}'.");
        }

        _environmentMonitor.EnsureReadyToStart(session.SourcePath, normalizedDestination, session.SourceIdentity.Length);

        if (_fileSystemService.FileExists(normalizedDestination) && !resumeOptions.OverwriteExisting)
        {
            throw new CopyException(CopyFailureKind.Permanent, $"Destination file already exists: '{normalizedDestination}'.");
        }

        return await ExecuteSessionAsync(session, resumeOptions, normalizedDestination, stopwatch, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<CopyResult> ExecuteSessionAsync(
        CopySession session,
        CopyOptions options,
        string destinationPath,
        Stopwatch stopwatch,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sessionRepository = _sessionRepositoryProvider.GetRepository(destinationPath);
        string? cacheDirectory = null;
        CopyResult? completedResult = null;

        try
        {
            await sessionRepository.SaveAsync(session, cancellationToken).ConfigureAwait(false);
            if (session.State is not CopyState.Paused)
            {
                ReportProgress(progress, session);
            }

            try
            {
                cacheDirectory = _stagingLocator.GetCacheDirectory(destinationPath);
                _fileSystemService.EnsureDirectory(cacheDirectory);

                session.State = CopyState.Running;
                await sessionRepository.SaveAsync(session, cancellationToken).ConfigureAwait(false);
                ReportProgress(progress, session);

                await _chunkCopyExecutor.ExecuteAsync(session, options, sessionRepository, progress, cancellationToken)
                    .ConfigureAwait(false);

                var currentIdentity = _sourceIdentityProvider.Capture(session.SourcePath);
                if (!currentIdentity.Matches(session.SourceIdentity))
                {
                    throw new SourceChangedException("Source file changed during transfer.");
                }

                session.State = CopyState.Verifying;
                await sessionRepository.SaveAsync(session, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Verifying session {SessionId}", session.SessionId);
                ReportProgress(progress, session);

                if (options.VerifyWholeFileAfterCopy)
                {
                    var sourceHash = await _hashService
                        .ComputeFileHashAsync(session.SourcePath, cancellationToken, options.IoBufferSize)
                        .ConfigureAwait(false);
                    var verified = await _fileVerifier.VerifyAsync(session.StagingPath, sourceHash, cancellationToken).ConfigureAwait(false);
                    if (!verified)
                    {
                        throw new IntegrityException("Final whole-file verification failed.");
                    }
                }

                _faultInjector.Apply(
                    FaultPoint.BeforeFinalization,
                    new FaultContext { SessionId = session.SessionId });

                _faultInjector.Apply(
                    FaultPoint.DuringFinalization,
                    new FaultContext { SessionId = session.SessionId });

                EnsureStagingFileLength(session, options);
                _fileSystemService.ReplaceOrMove(session.StagingPath, destinationPath, options.OverwriteExisting);
                _logger.LogInformation("Finalized session {SessionId} to {DestinationPath}", session.SessionId, destinationPath);

                _faultInjector.Apply(
                    FaultPoint.AfterFinalization,
                    new FaultContext { SessionId = session.SessionId });

                session.State = CopyState.Completed;
                await sessionRepository.SaveAsync(session, cancellationToken).ConfigureAwait(false);
                ReportProgress(progress, session);

                await sessionRepository.DeleteAsync(session.SessionId, cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();
                completedResult = new CopyResult(
                    session.SessionId,
                    session.SourcePath,
                    destinationPath,
                    CopyState.Completed,
                    session.SourceIdentity.Length,
                    stopwatch.Elapsed);
                _logger.LogInformation(
                    "Completed session {SessionId} in {ElapsedMs} ms",
                    session.SessionId,
                    stopwatch.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException)
            {
                session.State = CopyState.Paused;
                session.LastError = "Operation was cancelled.";
                await TryPersistSessionAsync(sessionRepository, session).ConfigureAwait(false);
                throw;
            }
            catch (CopyException copyException)
            {
                _logger.LogError(copyException, "Copy session {SessionId} failed with {FailureKind}", session.SessionId, copyException.FailureKind);
                session.State = CopyStateMapper.ResolveWaitingState(copyException);
                session.LastError = copyException.Message;
                await TryPersistSessionAsync(sessionRepository, session).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var classified = TransientErrorClassifier.Classify(exception, "Copy operation failed");
                session.State = CopyStateMapper.ResolveWaitingState(classified);
                session.LastError = classified.Message;
                await TryPersistSessionAsync(sessionRepository, session).ConfigureAwait(false);
                throw classified;
            }
        }
        finally
        {
            if (sessionRepository is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        if (completedResult is not null)
        {
            await _sessionCleanupService
                .CleanupSessionAsync(destinationPath, session.SessionId, cancellationToken)
                .ConfigureAwait(false);
        }

        return completedResult!;
    }

    private static CopyOptions CreateOptionsFromSession(CopySession session)
    {
        return new CopyOptions
        {
            ChunkSize = session.ChunkSize,
            VerifyWholeFileAfterCopy = true,
            FlushEveryChunk = true
        };
    }

    private static void ValidateOptions(CopyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ChunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Chunk size must be greater than zero.");
        }

        if (options.MaximumWorkers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum workers must be greater than zero.");
        }

        if (options.MaximumQueuedChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum queued chunks must be greater than zero.");
        }

        if (options.MaximumQueuedChunks < options.MaximumWorkers)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum queued chunks must be greater than or equal to maximum workers.");
        }

        if (options.MaximumChunkAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum chunk attempts must be greater than zero.");
        }

        if (options.RetryDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Retry delay cannot be negative.");
        }

        if (options.IoBufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "I/O buffer size must be greater than zero.");
        }
    }

    private CopySession CreateSession(
        string sourcePath,
        string destinationPath,
        SourceIdentity sourceIdentity,
        CopyOptions options,
        string? sessionId = null)
    {
        var chunks = ChunkPlanner.CreateChunks(sourceIdentity.Length, options.ChunkSize);
        var resolvedSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? Guid.NewGuid().ToString("N")
            : sessionId;

        var session = new CopySession
        {
            SessionId = resolvedSessionId,
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            SourceIdentity = sourceIdentity,
            StagingPath = _stagingLocator.GetPartFilePath(new CopySession
            {
                SessionId = resolvedSessionId,
                SourcePath = sourcePath,
                DestinationPath = destinationPath,
                SourceIdentity = sourceIdentity,
                StagingPath = string.Empty,
                ChunkSize = options.ChunkSize,
                TotalChunks = chunks.Count,
                Chunks = chunks.ToList(),
                State = CopyState.Pending
            }),
            ChunkSize = options.ChunkSize,
            TotalChunks = chunks.Count,
            Chunks = chunks.ToList(),
            State = CopyState.Pending
        };

        return session;
    }

    private void EnsureStagingFileLength(CopySession session, CopyOptions options)
    {
        if (session.SourceIdentity.Length <= 0 || !_fileSystemService.FileExists(session.StagingPath))
        {
            return;
        }

        var currentLength = _fileSystemService.GetMetadata(session.StagingPath).Length;
        if (currentLength == session.SourceIdentity.Length)
        {
            return;
        }

        using var stagingStream = _fileSystemService.OpenReadWrite(
            session.StagingPath,
            createNew: false,
            FileShare.None,
            options.IoBufferSize);
        stagingStream.SetLength(session.SourceIdentity.Length);
    }

    private async Task TryPersistSessionAsync(ISessionRepository sessionRepository, CopySession session)
    {
        try
        {
            await sessionRepository.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
        catch (DestinationUnavailableException exception)
        {
            _logger.LogWarning(
                exception,
                "Could not persist session {SessionId} because destination storage is unavailable.",
                session.SessionId);
        }
        catch (SessionPersistenceException exception)
        {
            _logger.LogWarning(
                exception,
                "Could not persist session {SessionId} because the session database is unavailable.",
                session.SessionId);
        }
    }

    private static void ReportProgress(IProgress<CopyProgress>? progress, CopySession session, int? currentChunkIndex = null)
    {
        progress?.Report(new CopyProgress(
            session.SessionId,
            session.State,
            session.CompletedBytes,
            session.SourceIdentity.Length,
            session.CompletedChunkCount,
            session.TotalChunks,
            currentChunkIndex));
    }
}
