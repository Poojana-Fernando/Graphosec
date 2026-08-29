namespace ResumableCopy.Core.Errors;

public sealed class InsufficientStorageException : CopyException
{
    public InsufficientStorageException(string message)
        : base(CopyFailureKind.Permanent, message)
    {
    }

    public InsufficientStorageException(string message, Exception innerException)
        : base(CopyFailureKind.Permanent, message, innerException)
    {
    }
}
