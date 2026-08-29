using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;

namespace ResumableCopy.Core.Resume;

public static class RecoveryFailureMapper
{
    public static CopyState ResolveWaitingState(RecoveryResult recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);

        if (recovery.State is CopyState.WaitingForSource
            or CopyState.WaitingForDestination
            or CopyState.WaitingForStorage
            or CopyState.RecoveryRequired)
        {
            return recovery.State;
        }

        var message = recovery.Message ?? string.Empty;
        if (message.Contains("Destination volume is not ready", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Destination storage is not available", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Destination volume is not ready for", StringComparison.OrdinalIgnoreCase))
        {
            return CopyState.WaitingForDestination;
        }

        if (message.Contains("Source volume is not ready", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Source file is no longer available", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Source file not found", StringComparison.OrdinalIgnoreCase))
        {
            return CopyState.WaitingForSource;
        }

        return CopyState.Failed;
    }

    public static CopyException CreateException(RecoveryResult recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);

        var message = recovery.Message ?? $"Session '{recovery.SessionId}' cannot be resumed.";
        return ResolveWaitingState(recovery) switch
        {
            CopyState.WaitingForDestination => new DestinationUnavailableException(message),
            CopyState.WaitingForSource => new SourceUnavailableException(message),
            CopyState.WaitingForStorage => new InsufficientStorageException(message),
            CopyState.RecoveryRequired => new SourceChangedException(message),
            _ => new CopyException(CopyFailureKind.Permanent, message),
        };
    }
}
