using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Abstractions;

public interface IStagingChunkValidator
{
    ValueTask<bool> ValidateChunkAsync(
        string stagingPath,
        ChunkRecord chunk,
        CancellationToken cancellationToken);
}
