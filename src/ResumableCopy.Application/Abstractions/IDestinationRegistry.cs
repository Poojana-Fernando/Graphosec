namespace ResumableCopy.Application.Abstractions;

public interface IDestinationRegistry
{
    Task RegisterAsync(string destinationPath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRegisteredAsync(CancellationToken cancellationToken = default);
}
