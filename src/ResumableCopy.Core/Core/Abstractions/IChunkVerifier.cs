namespace ResumableCopy.Core.Abstractions;

public interface IChunkVerifier
{
    bool Verify(ReadOnlySpan<byte> data, byte[] expectedHash);
}
