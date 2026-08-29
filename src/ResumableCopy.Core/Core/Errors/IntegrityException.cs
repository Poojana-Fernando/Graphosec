namespace ResumableCopy.Core.Errors;

public sealed class IntegrityException : CopyException
{
    public IntegrityException(string message)
        : base(CopyFailureKind.Permanent, message)
    {
    }

    public IntegrityException(string message, Exception innerException)
        : base(CopyFailureKind.Permanent, message, innerException)
    {
    }
}
