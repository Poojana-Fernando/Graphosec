namespace ResumableCopy.Core.Domain;

public sealed record FileMetadata(
    long Length,
    DateTime LastWriteTimeUtc,
    DateTime CreationTimeUtc,
    FileAttributes Attributes);
