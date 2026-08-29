using System.Security.Cryptography;
using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Core.Integrity;

public sealed class ChunkVerifier : IChunkVerifier
{
    private readonly IHashService _hashService;

    public ChunkVerifier(IHashService hashService)
    {
        _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
    }

    public bool Verify(ReadOnlySpan<byte> data, byte[] expectedHash)
    {
        ArgumentNullException.ThrowIfNull(expectedHash);

        var actualHash = _hashService.ComputeHash(data);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
