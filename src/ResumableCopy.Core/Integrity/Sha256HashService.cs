using System.Security.Cryptography;
using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Integrity;

public sealed class Sha256HashService : IHashService
{
    public string AlgorithmId => "sha256";

    public int HashSizeInBytes => SHA256.HashSizeInBytes;

    public byte[] ComputeHash(ReadOnlySpan<byte> data) => SHA256.HashData(data);

    public async ValueTask<byte[]> ComputeFileHashAsync(
        string path,
        CancellationToken cancellationToken,
        int ioBufferSize = CopyOptions.DefaultIoBufferSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: ioBufferSize,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}
