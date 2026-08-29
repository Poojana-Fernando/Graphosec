using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Abstractions;

public interface IStagingLocator
{
    string GetCacheDirectory(string destinationPath);

    string GetPartFilePath(CopySession session);
}
