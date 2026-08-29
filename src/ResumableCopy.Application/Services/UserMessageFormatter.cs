using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;

namespace ResumableCopy.Application.Services;

public static class UserMessageFormatter
{
    public static string GetStatusText(CopyState state) =>
        state switch
        {
            CopyState.Pending => "Preparing",
            CopyState.Running => "Copying",
            CopyState.Paused => "Paused",
            CopyState.WaitingForSource => "Waiting for source",
            CopyState.WaitingForDestination => "Waiting for device",
            CopyState.WaitingForStorage => "Insufficient space",
            CopyState.Verifying => "Verifying",
            CopyState.Completed => "Completed",
            CopyState.Failed => "Failed",
            CopyState.Cancelled => "Cancelled",
            CopyState.RecoveryRequired => "Recoverable error",
            _ => state.ToString()
        };

    public static string GetUserMessage(CopyState state, string? technicalMessage)
    {
        var headline = state switch
        {
            CopyState.WaitingForDestination =>
                "Connect the destination storage device and press Resume to continue.",
            CopyState.WaitingForSource =>
                "The source file is unavailable. Reconnect the source and press Resume.",
            CopyState.WaitingForStorage =>
                "There is not enough free space on the destination. Free space and press Resume.",
            CopyState.RecoveryRequired =>
                "The source file changed since the transfer started. Review the transfer before continuing.",
            CopyState.Paused =>
                "The transfer was paused. Press Resume to continue.",
            CopyState.Cancelled =>
                "The transfer was cancelled.",
            CopyState.Failed =>
                "The transfer failed.",
            CopyState.Completed =>
                "The transfer completed successfully.",
            _ => GetStatusText(state)
        };

        if (string.IsNullOrWhiteSpace(technicalMessage))
        {
            return headline;
        }

        return $"{headline}{Environment.NewLine}{Environment.NewLine}Details: {technicalMessage}";
    }

    public static string GetUserMessage(Exception exception)
    {
        if (exception is CopyException copyException)
        {
            return GetUserMessage(CopyStateMapper.ResolveWaitingState(copyException), copyException.Message);
        }

        return GetUserMessage(CopyState.Failed, exception.Message);
    }
}
