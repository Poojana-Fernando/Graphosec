namespace ResumableCopy.Core.Domain;

public sealed record RecoveryResult(
    string SessionId,
    CopyState State,
    bool CanResume,
    int InvalidatedChunkCount,
    string? Message,
    CopySession? Session = null);
