namespace ResumableCopy.Core.Errors;

public class CopyException : Exception
{
    public CopyException(CopyFailureKind failureKind, string message)
        : base(message)
    {
        FailureKind = failureKind;
    }

    public CopyException(CopyFailureKind failureKind, string message, Exception innerException)
        : base(message, innerException)
    {
        FailureKind = failureKind;
    }

    public CopyFailureKind FailureKind { get; }
}
