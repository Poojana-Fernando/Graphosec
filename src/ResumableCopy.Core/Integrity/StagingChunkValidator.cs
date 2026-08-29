using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Integrity;

public sealed class StagingChunkValidator : IStagingChunkValidator
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IChunkVerifier _chunkVerifier;

    public StagingChunkValidator(IFileSystemService fileSystemService, IChunkVerifier chunkVerifier)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _chunkVerifier = chunkVerifier ?? throw new ArgumentNullException(nameof(chunkVerifier));
    }

    public async ValueTask<bool> ValidateChunkAsync(
        string stagingPath,
        ChunkRecord chunk,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingPath);
        ArgumentNullException.ThrowIfNull(chunk);

        if (chunk.Hash is null || chunk.Hash.Length == 0)
        {
            return false;
        }

        if (!_fileSystemService.FileExists(stagingPath))
        {
            return false;
        }

        var metadata = _fileSystemService.GetMetadata(stagingPath);
        if (metadata.Length < chunk.Offset + chunk.Length)
        {
            return false;
        }

        await using var stream = _fileSystemService.OpenRead(stagingPath);
        stream.Seek(chunk.Offset, SeekOrigin.Begin);

        var buffer = new byte[chunk.Length];
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        if (totalRead != chunk.Length)
        {
            return false;
        }

        return _chunkVerifier.Verify(buffer, chunk.Hash);
    }
}
