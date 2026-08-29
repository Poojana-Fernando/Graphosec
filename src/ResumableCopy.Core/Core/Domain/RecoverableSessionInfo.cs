namespace ResumableCopy.Core.Domain;

public sealed record RecoverableSessionInfo(
    string SessionId,
    string SourcePath,
    string DestinationPath,
    CopyState State,
    long TotalBytes,
    long CompletedBytes,
    int CompletedChunks,
    int TotalChunks,
    string? LastError);
