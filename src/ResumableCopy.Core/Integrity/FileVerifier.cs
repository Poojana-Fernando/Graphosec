using System.Security.Cryptography;
using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Core.Integrity;

public sealed class FileVerifier : IFileVerifier
{
    private readonly IHashService _hashService;

    public FileVerifier(IHashService hashService)
    {
        _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
    }

    public async ValueTask<bool> VerifyAsync(string path, byte[] expectedHash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(expectedHash);

        var actualHash = await _hashService.ComputeFileHashAsync(path, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
