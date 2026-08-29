using ResumableCopy.Core.Domain;

namespace ResumableCopy.Application.Models;

public sealed class TransferSnapshot
{
    public required string SessionId { get; init; }

    public required string SourcePath { get; init; }

    public required string DestinationPath { get; init; }

    public CopyState State { get; init; }

    public long BytesCopied { get; init; }

    public long TotalBytes { get; init; }

    public int CompletedChunks { get; init; }

    public int TotalChunks { get; init; }

    public double BytesPerSecond { get; init; }

    public TimeSpan? EstimatedTimeRemaining { get; init; }

    public string StatusText { get; init; } = string.Empty;

    public string? ErrorMessage { get; init; }

    public bool CanPause { get; init; }

    public bool CanResume { get; init; }

    public bool CanCancel { get; init; }

    public bool CanRetry { get; init; }

    public bool CanRemove { get; init; }

    public double ProgressPercent =>
        TotalBytes <= 0 ? (State == CopyState.Completed ? 100d : 0d) : BytesCopied * 100d / TotalBytes;
}
