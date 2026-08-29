namespace ResumableCopy.Core.Abstractions;

public interface IFileIdentityProvider
{
    (ulong? VolumeSerial, ulong? FileId) TryGetIdentity(string path);
}
