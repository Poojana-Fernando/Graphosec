using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Models;

namespace ResumableCopy.Application.Services;

public sealed class NullTransferHistoryStore : ITransferHistoryStore
{
    public Task<IReadOnlyList<TransferHistoryRecord>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TransferHistoryRecord>>([]);

    public Task UpsertAsync(TransferHistoryRecord record, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RemoveFinishedAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
