using System.Collections.Concurrent;
using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Storage;

public sealed class InMemorySessionRepository : ISessionRepository
{
    private readonly ConcurrentDictionary<string, CopySession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _mutationLock = new();

    public ValueTask<CopySession?> FindAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _sessions.TryGetValue(sessionId, out var session);
        return ValueTask.FromResult<CopySession?>(session is null ? null : CloneSession(session));
    }

    public ValueTask<IReadOnlyList<CopySession>> FindUnfinishedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var unfinished = _sessions.Values
            .Where(static session => session.State is not CopyState.Completed and not CopyState.Cancelled)
            .Select(CloneSession)
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<CopySession>>(unfinished);
    }

    public ValueTask SaveAsync(CopySession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(session);

        lock (_mutationLock)
        {
            session.UpdatedAtUtc = DateTimeOffset.UtcNow;
            _sessions[session.SessionId] = CloneSession(session);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkChunkCompleteAsync(string sessionId, ChunkRecord chunk, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(chunk);

        lock (_mutationLock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
            }

            var existingChunk = session.Chunks.FirstOrDefault(existing => existing.Index == chunk.Index)
                ?? throw new InvalidOperationException($"Chunk {chunk.Index} was not found in session '{sessionId}'.");

            existingChunk.Hash = chunk.Hash;
            existingChunk.IsComplete = true;
            existingChunk.ExecutionState = ChunkExecutionState.Completed;
            session.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkChunkPendingAsync(string sessionId, ChunkRecord chunk, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(chunk);

        lock (_mutationLock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                throw new KeyNotFoundException($"Session '{sessionId}' was not found.");
            }

            var existingChunk = session.Chunks.FirstOrDefault(existing => existing.Index == chunk.Index)
                ?? throw new InvalidOperationException($"Chunk {chunk.Index} was not found in session '{sessionId}'.");

            existingChunk.Hash = null;
            existingChunk.IsComplete = false;
            existingChunk.ExecutionState = ChunkExecutionState.Pending;
            session.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions.TryRemove(sessionId, out _);
        return ValueTask.CompletedTask;
    }

    private static CopySession CloneSession(CopySession session)
    {
        return new CopySession
        {
            SessionId = session.SessionId,
            SourcePath = session.SourcePath,
            DestinationPath = session.DestinationPath,
            SourceIdentity = session.SourceIdentity,
            StagingPath = session.StagingPath,
            ChunkSize = session.ChunkSize,
            TotalChunks = session.TotalChunks,
            Chunks = session.Chunks
                .Select(chunk => new ChunkRecord
                {
                    Index = chunk.Index,
                    Offset = chunk.Offset,
                    Length = chunk.Length,
                    Hash = chunk.Hash is null ? null : (byte[])chunk.Hash.Clone(),
                    IsComplete = chunk.IsComplete,
                    ExecutionState = chunk.ExecutionState
                })
                .ToList(),
            State = session.State,
            CreatedAtUtc = session.CreatedAtUtc,
            UpdatedAtUtc = session.UpdatedAtUtc,
            LastError = session.LastError
        };
    }
}
