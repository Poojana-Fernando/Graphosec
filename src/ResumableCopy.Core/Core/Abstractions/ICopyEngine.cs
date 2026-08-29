using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Abstractions;

public interface ICopyEngine
{
    Task<CopyResult> CopyAsync(
        CopyJob job,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken);

    Task<CopyResult> ResumeAsync(
        string sessionId,
        string destinationPath,
        CopyOptions? options,
        IProgress<CopyProgress>? progress,
        CancellationToken cancellationToken);
}
