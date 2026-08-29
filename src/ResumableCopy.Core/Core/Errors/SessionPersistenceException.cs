namespace ResumableCopy.Core.Errors;

public sealed class SessionPersistenceException : CopyException
{
    public SessionPersistenceException(string message)
        : base(CopyFailureKind.Permanent, message)
    {
    }

    public SessionPersistenceException(string message, Exception innerException)
        : base(CopyFailureKind.Permanent, message, innerException)
    {
    }
}
