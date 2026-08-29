namespace ResumableCopy.Core.Domain;

public sealed record SourceIdentity(
    long Length,
    DateTime LastWriteTimeUtc,
    DateTime CreationTimeUtc,
    ulong? VolumeSerial = null,
    ulong? FileId = null)
{
    public bool Matches(SourceIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Length == other.Length
            && LastWriteTimeUtc == other.LastWriteTimeUtc
            && CreationTimeUtc == other.CreationTimeUtc
            && VolumeSerial == other.VolumeSerial
            && FileId == other.FileId;
    }
}
