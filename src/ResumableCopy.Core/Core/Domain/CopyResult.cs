namespace ResumableCopy.Core.Domain;

public sealed record CopyResult(
    string SessionId,
    string SourcePath,
    string DestinationPath,
    CopyState FinalState,
    long BytesCopied,
    TimeSpan Duration);
