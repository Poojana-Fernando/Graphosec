namespace ResumableCopy.Core.Domain;

public enum CopyState
{
    Pending,
    Running,
    Paused,
    WaitingForSource,
    WaitingForDestination,
    WaitingForStorage,
    Verifying,
    Completed,
    Failed,
    Cancelled,
    RecoveryRequired
}
