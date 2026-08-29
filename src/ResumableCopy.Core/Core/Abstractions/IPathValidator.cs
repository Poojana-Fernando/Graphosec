namespace ResumableCopy.Core.Abstractions;

public interface IPathValidator
{
    (string SourcePath, string DestinationPath) ValidateCopyPaths(string sourcePath, string destinationPath);
}
