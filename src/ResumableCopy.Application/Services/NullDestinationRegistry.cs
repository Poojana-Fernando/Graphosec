using ResumableCopy.Application.Abstractions;

namespace ResumableCopy.Application.Services;

internal sealed class NullDestinationRegistry : IDestinationRegistry
{
    public Task RegisterAsync(string destinationPath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetRegisteredAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
