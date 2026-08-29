using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;

namespace ResumableCopy.Core.Concurrency;

public interface IChunkCopyExecutor
{
    Task ExecuteAsync(
        CopySession session,
        CopyOptions options,
        ISessionRepository sessionRepository,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken);
}
