using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Abstractions;

public interface ISourceIdentityProvider
{
    SourceIdentity Capture(string path);
}
