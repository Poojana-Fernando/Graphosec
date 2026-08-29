using System.Buffers;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Reliability;

namespace ResumableCopy.Core.Concurrency;

public sealed class ParallelChunkCopyExecutor : IChunkCopyExecutor
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IHashService _hashService;
    private readonly IChunkVerifier _chunkVerifier;
    private readonly ITransferEnvironmentMonitor _environmentMonitor;
    private readonly IFaultInjector _faultInjector;
    private readonly ILogger<ParallelChunkCopyExecutor> _logger;

    public ParallelChunkCopyExecutor(
        IFileSystemService fileSystemService,
        IHashService hashService,
        IChunkVerifier chunkVerifier,
        ITransferEnvironmentMonitor environmentMonitor,
        IFaultInjector? faultInjector = null,
        ILogger<ParallelChunkCopyExecutor>? logger = null)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
        _chunkVerifier = chunkVerifier ?? throw new ArgumentNullException(nameof(chunkVerifier));
        _environmentMonitor = environmentMonitor ?? throw new ArgumentNullException(nameof(environmentMonitor));
        _faultInjector = faultInjector ?? NullFaultInjector.Instance;
        _logger = logger ?? NullLogger<ParallelChunkCopyExecutor>.Instance;
    }

    public async Task ExecuteAsync(
        CopySession session,
        CopyOptions options,
        ISessionRepository sessionRepository,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessionRepository);

        ReportPreparingProgress(progress, session);
        PrepareStagingFile(session, options);

        var pendingChunks = session.Chunks
            .Where(static chunk => !chunk.IsComplete)
            .OrderBy(static chunk => chunk.Index)
            .ToArray();

        if (pendingChunks.Length == 0)
        {
            return;
        }

        if (options.MaximumWorkers == 1)
        {
            await ExecuteSequentialAsync(session, options, sessionRepository, progress, pendingChunks, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await ExecuteParallelAsync(session, options, sessionRepository, progress, pendingChunks, cancellationToken)
            .ConfigureAwait(false);
    }

    private void PrepareStagingFile(CopySession session, CopyOptions options)
    {
        var createNew = !_fileSystemService.FileExists(session.StagingPath);
        using var stagingStream = _fileSystemService.OpenWrite(session.StagingPath, createNew, options.IoBufferSize);

        if (session.SourceIdentity.Length > 0
            && _fileSystemService.SupportsSparsePreallocation(session.StagingPath))
        {
            stagingStream.SetLength(session.SourceIdentity.Length);
        }
    }

    private static void ReportPreparingProgress(IProgress<CopyProgress>? progress, CopySession session)
    {
        if (progress is null)
        {
            return;
        }

        progress.Report(new CopyProgress(
            session.SessionId,
            CopyState.Pending,
            session.CompletedBytes,
            session.SourceIdentity.Length,
            session.CompletedChunkCount,
            session.TotalChunks));
    }

    private async Task ExecuteSequentialAsync(
        CopySession session,
        CopyOptions options,
        ISessionRepository sessionRepository,
        IProgress<CopyProgress>? progress,
        IReadOnlyList<ChunkRecord> pendingChunks,
        CancellationToken cancellationToken)
    {
        var coordinator = new ChunkWorkCoordinator();
        var flushTracker = new ChunkFlushTracker();
        var buffer = ArrayPool<byte>.Shared.Rent(options.ChunkSize);

        try
        {
            await using var sourceStream = _fileSystemService.OpenRead(session.SourcePath, options.IoBufferSize);
            await using var destinationStream = _fileSystemService.OpenReadWrite(
                session.StagingPath,
                createNew: false,
                FileShare.ReadWrite,
                options.IoBufferSize);

            foreach (var chunk in pendingChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!coordinator.TryBegin(chunk))
                {
                    continue;
                }

                _environmentMonitor.EnsureReadyForChunk(session, session.DestinationPath);

                try
                {
                    await CopyChunkWithVerificationAsync(
                        session,
                        sourceStream,
                        destinationStream,
                        chunk,
                        buffer,
                        options,
                        sessionRepository,
                        coordinator,
                        flushTracker,
                        cancellationToken).ConfigureAwait(false);

                    ReportProgress(progress, session, chunk.Index);
                }
                catch
                {
                    coordinator.ResetToPending(chunk);
                    throw;
                }
            }

            await flushTracker.FlushRemainingAsync(destinationStream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task ExecuteParallelAsync(
        CopySession session,
        CopyOptions options,
        ISessionRepository sessionRepository,
        IProgress<CopyProgress>? progress,
        IReadOnlyList<ChunkRecord> pendingChunks,
        CancellationToken cancellationToken)
    {
        var coordinator = new ChunkWorkCoordinator();
        var flushTracker = new ChunkFlushTracker();
        var progressLock = new object();
        var channel = Channel.CreateBounded<ChunkRecord>(new BoundedChannelOptions(options.MaximumQueuedChunks)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });

        Exception? workerException = null;
        var workers = new Task[options.MaximumWorkers];

        for (var workerIndex = 0; workerIndex < options.MaximumWorkers; workerIndex++)
        {
            workers[workerIndex] = WorkerLoopAsync(
                session,
                options,
                sessionRepository,
                progress,
                channel.Reader,
                coordinator,
                flushTracker,
                progressLock,
                exception =>
                {
                    Interlocked.CompareExchange(ref workerException, exception, null);
                    channel.Writer.TryComplete(exception);
                },
                cancellationToken);
        }

        try
        {
            foreach (var chunk in pendingChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await channel.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    break;
                }
            }

            channel.Writer.TryComplete();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Interlocked.CompareExchange(ref workerException, exception, null);
            channel.Writer.TryComplete(exception);
        }

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Interlocked.CompareExchange(ref workerException, exception, null);
        }

        if (workerException is not null)
        {
            throw workerException;
        }
    }

    private async Task WorkerLoopAsync(
        CopySession session,
        CopyOptions options,
        ISessionRepository sessionRepository,
        IProgress<CopyProgress>? progress,
        ChannelReader<ChunkRecord> reader,
        ChunkWorkCoordinator coordinator,
        ChunkFlushTracker flushTracker,
        object progressLock,
        Action<Exception> reportFailure,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(options.ChunkSize);

        try
        {
            await using var sourceStream = _fileSystemService.OpenRead(session.SourcePath, options.IoBufferSize);
            await using var destinationStream = _fileSystemService.OpenReadWrite(
                session.StagingPath,
                createNew: false,
                FileShare.ReadWrite,
                options.IoBufferSize);

            await foreach (var chunk in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!coordinator.TryBegin(chunk))
                {
                    continue;
                }

                _environmentMonitor.EnsureReadyForChunk(session, session.DestinationPath);

                try
                {
                    await CopyChunkWithVerificationAsync(
                        session,
                        sourceStream,
                        destinationStream,
                        chunk,
                        buffer,
                        options,
                        sessionRepository,
                        coordinator,
                        flushTracker,
                        cancellationToken).ConfigureAwait(false);

                    lock (progressLock)
                    {
                        ReportProgress(progress, session, chunk.Index);
                    }
                }
                catch (Exception exception)
                {
                    coordinator.ResetToPending(chunk);
                    reportFailure(exception);
                    throw;
                }
            }
            await flushTracker.FlushRemainingAsync(destinationStream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            reportFailure(exception);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task CopyChunkWithVerificationAsync(
        CopySession session,
        Stream sourceStream,
        Stream destinationStream,
        ChunkRecord chunk,
        byte[] buffer,
        CopyOptions options,
        ISessionRepository sessionRepository,
        ChunkWorkCoordinator coordinator,
        ChunkFlushTracker flushTracker,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < options.MaximumChunkAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var faultContext = CreateFaultContext(session, chunk, attempt, buffer);

                _faultInjector.Apply(FaultPoint.BeforeChunkRead, faultContext);
                sourceStream.Seek(chunk.Offset, SeekOrigin.Begin);
                destinationStream.Seek(chunk.Offset, SeekOrigin.Begin);

                var bytesRead = await ReadExactAsync(sourceStream, buffer.AsMemory(0, chunk.Length), cancellationToken)
                    .ConfigureAwait(false);

                if (bytesRead != chunk.Length)
                {
                    throw new IntegrityException(
                        $"Expected to read {chunk.Length} bytes for chunk {chunk.Index}, but read {bytesRead}.");
                }

                _faultInjector.Apply(FaultPoint.AfterChunkRead, faultContext with { Buffer = buffer.AsMemory(0, chunk.Length) });

                var chunkData = buffer.AsMemory(0, chunk.Length);
                _faultInjector.Apply(FaultPoint.BeforeChunkHash, faultContext with { Buffer = chunkData });
                var expectedHash = _hashService.ComputeHash(chunkData.Span);
                _faultInjector.Apply(FaultPoint.AfterChunkHash, faultContext with { Buffer = chunkData });

                _faultInjector.Apply(FaultPoint.BeforeChunkWrite, faultContext with { Buffer = chunkData });
                await destinationStream.WriteAsync(chunkData, cancellationToken).ConfigureAwait(false);
                _faultInjector.Apply(FaultPoint.AfterChunkWrite, faultContext with { Buffer = chunkData });

                await flushTracker.TrackAndMaybeFlushAsync(
                    destinationStream,
                    chunk.Length,
                    options.FlushEveryChunk,
                    options.FlushIntervalBytes,
                    cancellationToken).ConfigureAwait(false);

                destinationStream.Seek(chunk.Offset, SeekOrigin.Begin);
                var readBackBytes = await ReadExactAsync(destinationStream, buffer.AsMemory(0, chunk.Length), cancellationToken)
                    .ConfigureAwait(false);

                if (readBackBytes != chunk.Length)
                {
                    throw new IntegrityException(
                        $"Read-back verification failed for chunk {chunk.Index}: expected {chunk.Length} bytes, read {readBackBytes}.");
                }

                _faultInjector.Apply(FaultPoint.BeforeChunkVerify, faultContext with { Buffer = buffer.AsMemory(0, chunk.Length) });
                if (!_chunkVerifier.Verify(buffer.AsSpan(0, chunk.Length), expectedHash))
                {
                    throw new IntegrityException($"Chunk {chunk.Index} failed hash verification.");
                }

                _faultInjector.Apply(FaultPoint.AfterChunkVerify, faultContext with { Buffer = buffer.AsMemory(0, chunk.Length) });

                chunk.Hash = expectedHash;
                chunk.IsComplete = true;
                coordinator.MarkCompleted(chunk);

                try
                {
                    await sessionRepository.MarkChunkCompleteAsync(session.SessionId, chunk, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    chunk.Hash = null;
                    chunk.IsComplete = false;
                    coordinator.ResetToPending(chunk);
                    throw;
                }

                return;
            }
            catch (Exception exception) when (ShouldRetry(exception, attempt, options.MaximumChunkAttempts))
            {
                _logger.LogWarning(
                    exception,
                    "Chunk {ChunkIndex} for session {SessionId} failed attempt {AttemptNumber}; retrying.",
                    chunk.Index,
                    session.SessionId,
                    attempt + 1);
                await Task.Delay(options.RetryDelayMilliseconds * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogError(
            "Chunk {ChunkIndex} for session {SessionId} failed after {MaximumAttempts} attempts.",
            chunk.Index,
            session.SessionId,
            options.MaximumChunkAttempts);
        throw new IntegrityException($"Chunk {chunk.Index} failed verification after retry.");
    }

    private static FaultContext CreateFaultContext(
        CopySession session,
        ChunkRecord chunk,
        int attemptNumber,
        byte[] buffer) =>
        new()
        {
            SessionId = session.SessionId,
            ChunkIndex = chunk.Index,
            AttemptNumber = attemptNumber,
            Buffer = buffer.AsMemory(0, chunk.Length)
        };

    private static bool ShouldRetry(Exception exception, int attempt, int maximumAttempts)
    {
        if (attempt >= maximumAttempts - 1)
        {
            return false;
        }

        return exception is IntegrityException or DestinationUnavailableException;
    }

    private static async Task<int> ReadExactAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[totalRead..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
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
