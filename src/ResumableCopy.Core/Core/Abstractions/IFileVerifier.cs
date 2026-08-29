namespace ResumableCopy.Core.Abstractions;

public interface IFileVerifier
{
    ValueTask<bool> VerifyAsync(string path, byte[] expectedHash, CancellationToken cancellationToken);
}
