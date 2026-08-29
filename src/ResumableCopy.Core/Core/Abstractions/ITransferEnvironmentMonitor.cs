using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Abstractions;

public interface ITransferEnvironmentMonitor
{
    void EnsureReadyToStart(string sourcePath, string destinationPath, long totalBytes);

    void EnsureReadyForChunk(CopySession session, string destinationPath);

    void EnsureSourceIdentityUnchanged(CopySession session);
}
