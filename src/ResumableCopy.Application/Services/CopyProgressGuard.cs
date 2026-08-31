using ResumableCopy.Core.Domain;

namespace ResumableCopy.Application.Services;

internal static class CopyProgressGuard
{
    public static bool ShouldApplyProgress(CopyState currentState, CopyState incomingState)
    {
        if (currentState is CopyState.Cancelled or CopyState.Completed or CopyState.Failed)
        {
            return false;
        }

        if (incomingState is CopyState.Running or CopyState.Verifying
            && currentState is CopyState.Paused)
        {
            return true;
        }

        if (incomingState is CopyState.Paused
            && currentState is CopyState.Running or CopyState.Verifying)
        {
            return false;
        }

        if (currentState is CopyState.Running or CopyState.Pending or CopyState.Verifying)
        {
            return true;
        }

        return incomingState is not CopyState.Running
            and not CopyState.Pending
            and not CopyState.Verifying;
    }
}
