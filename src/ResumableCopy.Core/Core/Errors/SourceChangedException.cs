namespace ResumableCopy.Core.Errors;

public sealed class SourceChangedException : CopyException
{
    public SourceChangedException(string message)
        : base(CopyFailureKind.Permanent, message)
    {
    }
}
