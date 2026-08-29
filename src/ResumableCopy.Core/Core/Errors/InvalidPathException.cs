namespace ResumableCopy.Core.Errors;

public sealed class InvalidPathException : CopyException
{
    public InvalidPathException(string message)
        : base(CopyFailureKind.Permanent, message)
    {
    }
}
