namespace ResumableCopy.Core.Errors;

public sealed class PermissionDeniedException : CopyException
{
    public PermissionDeniedException(string message)
        : base(CopyFailureKind.Permanent, message)
    {
    }

    public PermissionDeniedException(string message, Exception innerException)
        : base(CopyFailureKind.Permanent, message, innerException)
    {
    }
}
