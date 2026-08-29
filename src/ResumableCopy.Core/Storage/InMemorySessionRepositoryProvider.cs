using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Core.Storage;

public sealed class InMemorySessionRepositoryProvider : ISessionRepositoryProvider
{
    public InMemorySessionRepositoryProvider(InMemorySessionRepository? repository = null)
    {
        Repository = repository ?? new InMemorySessionRepository();
    }

    public InMemorySessionRepository Repository { get; }

    public ISessionRepository GetRepository(string destinationPath) => Repository;
}
