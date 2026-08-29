using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Abstractions;

public interface ICopyPerformanceAdvisor
{
    CopyOptions ResolveOptions(long fileSizeBytes, CopyOptions requestedOptions);
}
