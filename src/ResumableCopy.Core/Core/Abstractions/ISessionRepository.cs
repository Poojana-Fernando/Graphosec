using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Abstractions;

public interface ISessionRepository
{
    ValueTask<CopySession?> FindAsync(string sessionId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<CopySession>> FindUnfinishedAsync(CancellationToken cancellationToken);

    ValueTask SaveAsync(CopySession session, CancellationToken cancellationToken);

    ValueTask MarkChunkCompleteAsync(string sessionId, ChunkRecord chunk, CancellationToken cancellationToken);

    ValueTask MarkChunkPendingAsync(string sessionId, ChunkRecord chunk, CancellationToken cancellationToken);

    ValueTask DeleteAsync(string sessionId, CancellationToken cancellationToken);
}
