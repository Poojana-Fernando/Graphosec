using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Abstractions;

public interface ITransferRecoveryService
{
    ValueTask<IReadOnlyList<RecoverableSessionInfo>> DiscoverRecoverableSessionsAsync(
        string destinationPath,
        CancellationToken cancellationToken);

    ValueTask<RecoveryResult> RecoverSessionAsync(
        string destinationPath,
        string sessionId,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<RecoveryResult>> RecoverUnfinishedSessionsAsync(
        string destinationPath,
        CancellationToken cancellationToken);
}
