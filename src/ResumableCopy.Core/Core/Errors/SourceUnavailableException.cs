namespace ResumableCopy.Core.Errors;

public sealed class SourceUnavailableException : CopyException
{
    public SourceUnavailableException(string message)
        : base(CopyFailureKind.Recoverable, message)
    {
    }

    public SourceUnavailableException(string message, Exception innerException)
        : base(CopyFailureKind.Recoverable, message, innerException)
    {
    }
}
