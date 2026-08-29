using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Abstractions;

public interface IHashService
{
    string AlgorithmId { get; }

    int HashSizeInBytes { get; }

    byte[] ComputeHash(ReadOnlySpan<byte> data);

    ValueTask<byte[]> ComputeFileHashAsync(
        string path,
        CancellationToken cancellationToken,
        int ioBufferSize = CopyOptions.DefaultIoBufferSize);
}
