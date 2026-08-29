using ResumableCopy.Application.Models;

namespace ResumableCopy.Application.Abstractions;

public interface ITransferHistoryStore
{
    Task<IReadOnlyList<TransferHistoryRecord>> LoadAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(TransferHistoryRecord record, CancellationToken cancellationToken = default);

    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);

    Task RemoveFinishedAsync(CancellationToken cancellationToken = default);
}
