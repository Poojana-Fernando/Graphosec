namespace ResumableCopy.Core.Errors;

public sealed class DestinationUnavailableException : CopyException
{
    public DestinationUnavailableException(string message)
        : base(CopyFailureKind.Recoverable, message)
    {
    }

    public DestinationUnavailableException(string message, Exception innerException)
        : base(CopyFailureKind.Recoverable, message, innerException)
    {
    }
}
