using ResumableCopy.Application.Models;

using ResumableCopy.Core.Domain;



namespace ResumableCopy.Application.Abstractions;



public interface ITransferOrchestrator : IDisposable

{

    event EventHandler<TransferSnapshot>? TransferChanged;



    event EventHandler<string>? TransferRemoved;



    IReadOnlyList<TransferSnapshot> GetTransfers();



    TransferSnapshot? GetTransfer(string sessionId);



    Task<string> StartCopyAsync(

        string sourcePath,

        string destinationPath,

        CopyOptions options,

        CancellationToken cancellationToken = default);



    Task ResumeAsync(

        string sessionId,

        string destinationPath,

        CopyOptions? options = null,

        CancellationToken cancellationToken = default);



    void RequestPause(string sessionId);



    Task CancelTransferAsync(string sessionId, CancellationToken cancellationToken = default);



    Task DiscoverRecoverableSessionsAsync(string destinationDirectory, CancellationToken cancellationToken = default);



    Task RecoverSessionAsync(

        string destinationPath,

        string sessionId,

        CancellationToken cancellationToken = default);



    Task RemoveTransferAsync(string sessionId, CancellationToken cancellationToken = default);



    Task ClearFinishedTransfersAsync(CancellationToken cancellationToken = default);



    Task LoadPersistedHistoryAsync(CancellationToken cancellationToken = default);



    void NotifyVolumesChanged();

}

