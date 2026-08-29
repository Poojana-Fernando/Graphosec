using ResumableCopy.Core.Domain;

namespace ResumableCopy.Core.Abstractions;

public interface IFileSystemService
{
    bool FileExists(string path);

    FileMetadata GetMetadata(string path);

    Stream OpenRead(string path, int ioBufferSize = CopyOptions.DefaultIoBufferSize);

    Stream OpenWrite(string path, bool createNew, int ioBufferSize = CopyOptions.DefaultIoBufferSize);

    Stream OpenReadWrite(string path, bool createNew, FileShare share, int ioBufferSize = CopyOptions.DefaultIoBufferSize);

    void EnsureDirectory(string path);

    void ReplaceOrMove(string sourcePath, string destinationPath, bool overwrite);

    void Delete(string path);

    long GetAvailableFreeSpace(string path);

    bool SupportsSparsePreallocation(string path);

    bool IsSameVolume(string pathA, string pathB);

    void ValidatePathWithinRoot(string path, string rootPath);
}
