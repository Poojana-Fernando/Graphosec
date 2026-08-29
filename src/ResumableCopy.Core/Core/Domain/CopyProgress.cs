namespace ResumableCopy.Core.Domain;

public sealed record CopyProgress(
    string SessionId,
    CopyState State,
    long BytesCopied,
    long TotalBytes,
    int CompletedChunks,
    int TotalChunks,
    int? CurrentChunkIndex = null);
