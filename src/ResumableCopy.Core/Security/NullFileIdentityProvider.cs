using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Core.Security;

public sealed class NullFileIdentityProvider : IFileIdentityProvider
{
    public static NullFileIdentityProvider Instance { get; } = new();

    private NullFileIdentityProvider()
    {
    }

    public (ulong? VolumeSerial, ulong? FileId) TryGetIdentity(string path) => (null, null);
}
