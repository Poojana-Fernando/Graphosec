namespace ResumableCopy.Core.Abstractions;

public interface ISessionCleanupService
{
    ValueTask CleanupSessionAsync(
        string destinationPath,
        string sessionId,
        CancellationToken cancellationToken = default);
}
