namespace ResumableCopy.Core.Abstractions;

public interface IDiskSpaceManager
{
    void EnsureSufficientSpace(string destinationPath, long requiredBytes);

    void EnsureRemainingSpace(string destinationPath, long remainingBytes);

    long GetAvailableFreeSpace(string destinationPath);

    bool HasRemainingSpace(string destinationPath, long remainingBytes);

    void InvalidateCache(string? destinationPath = null);
}
