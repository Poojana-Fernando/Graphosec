using ResumableCopy.Core.Domain;

namespace ResumableCopy.Application.Models;

public sealed class TransferHistoryRecord
{
    public required string SessionId { get; init; }

    public required string SourcePath { get; init; }

    public required string DestinationPath { get; init; }

    public CopyState State { get; init; }

    public long BytesCopied { get; init; }

    public long TotalBytes { get; init; }

    public int CompletedChunks { get; init; }

    public int TotalChunks { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public TransferSnapshot ToSnapshot() =>
        new()
        {
            SessionId = SessionId,
            SourcePath = SourcePath,
            DestinationPath = DestinationPath,
            State = State,
            BytesCopied = BytesCopied,
            TotalBytes = TotalBytes,
            CompletedChunks = CompletedChunks,
            TotalChunks = TotalChunks,
            BytesPerSecond = 0,
            EstimatedTimeRemaining = null,
            StatusText = Services.UserMessageFormatter.GetStatusText(State),
            ErrorMessage = ErrorMessage,
            CanPause = State is CopyState.Running or CopyState.Verifying,
            CanResume = State is CopyState.Paused
                or CopyState.WaitingForSource
                or CopyState.WaitingForDestination
                or CopyState.WaitingForStorage
                or CopyState.RecoveryRequired
                or CopyState.Failed,
            CanCancel = false,
            CanRetry = State is CopyState.RecoveryRequired or CopyState.Failed,
            CanRemove = State is not (CopyState.Running or CopyState.Verifying or CopyState.Pending)
        };

    public static TransferHistoryRecord FromSnapshot(TransferSnapshot snapshot) =>
        new()
        {
            SessionId = snapshot.SessionId,
            SourcePath = snapshot.SourcePath,
            DestinationPath = snapshot.DestinationPath,
            State = snapshot.State,
            BytesCopied = snapshot.BytesCopied,
            TotalBytes = snapshot.TotalBytes,
            CompletedChunks = snapshot.CompletedChunks,
            TotalChunks = snapshot.TotalChunks,
            ErrorMessage = snapshot.ErrorMessage,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
}
