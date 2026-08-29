using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Core.Abstractions;

public interface ISessionRepositoryProvider
{
    ISessionRepository GetRepository(string destinationPath);
}
