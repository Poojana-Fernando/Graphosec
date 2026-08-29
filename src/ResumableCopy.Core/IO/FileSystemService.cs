using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Security;

namespace ResumableCopy.Core.IO;

public sealed class FileSystemService : IFileSystemService
{
    private readonly IReparsePointInspector _reparsePointInspector;

    public FileSystemService(IReparsePointInspector? reparsePointInspector = null)
    {
        _reparsePointInspector = reparsePointInspector ?? PlatformSecurityServices.CreateReparsePointInspector();
    }

    public bool FileExists(string path)
    {
        var normalizedPath = NormalizePath(path);
        return File.Exists(normalizedPath);
    }

    public FileMetadata GetMetadata(string path)
    {
        var normalizedPath = NormalizePath(path);
        _reparsePointInspector.EnsureRegularFile(normalizedPath);

        try
        {
            var fileInfo = new FileInfo(normalizedPath);
            var metadata = new FileMetadata(
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc,
                fileInfo.CreationTimeUtc,
                fileInfo.Attributes);

            _reparsePointInspector.EnsureRegularFile(normalizedPath);
            return metadata;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new PermissionDeniedException($"Access denied reading metadata for '{normalizedPath}'.", exception);
        }
    }

    public Stream OpenRead(string path, int ioBufferSize = CopyOptions.DefaultIoBufferSize)
    {
        var normalizedPath = NormalizePath(path);
        _reparsePointInspector.EnsureRegularFile(normalizedPath);

        try
        {
            var ioPath = PathNormalization.ExpandForIo(normalizedPath);
            var stream = new FileStream(
                ioPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: ioBufferSize,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            _reparsePointInspector.EnsureRegularFile(normalizedPath);
            return stream;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new PermissionDeniedException($"Access denied reading '{normalizedPath}'.", exception);
        }
    }

    public Stream OpenWrite(string path, bool createNew, int ioBufferSize = CopyOptions.DefaultIoBufferSize) =>
        OpenReadWrite(path, createNew, FileShare.None, ioBufferSize);

    public Stream OpenReadWrite(string path, bool createNew, FileShare share, int ioBufferSize = CopyOptions.DefaultIoBufferSize)
    {
        var normalizedPath = NormalizePath(path);
        EnsureParentDirectory(normalizedPath);

        try
        {
            var ioPath = PathNormalization.ExpandForIo(normalizedPath);
            return new FileStream(
                ioPath,
                createNew ? FileMode.CreateNew : FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                share,
                bufferSize: ioBufferSize,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new PermissionDeniedException($"Access denied writing '{normalizedPath}'.", exception);
        }
    }

    public void EnsureDirectory(string path)
    {
        var normalizedPath = NormalizePath(path);
        Directory.CreateDirectory(normalizedPath);
    }

    public void ReplaceOrMove(string sourcePath, string destinationPath, bool overwrite)
    {
        var normalizedSource = NormalizePath(sourcePath);
        var normalizedDestination = NormalizePath(destinationPath);

        if (!IsSameVolume(normalizedSource, normalizedDestination))
        {
            throw new CopyException(
                CopyFailureKind.Permanent,
                "Atomic finalization is unavailable because staging and destination are on different volumes.");
        }

        EnsureParentDirectory(normalizedDestination);

        try
        {
            if (File.Exists(normalizedDestination))
            {
                if (!overwrite)
                {
                    throw new IOException($"Destination file already exists: '{normalizedDestination}'.");
                }

                var backupPath = normalizedDestination + ".bak";
                File.Replace(normalizedSource, normalizedDestination, backupPath, ignoreMetadataErrors: true);

                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                return;
            }

            File.Move(normalizedSource, normalizedDestination);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new PermissionDeniedException(
                $"Access denied finalizing '{normalizedDestination}'.",
                exception);
        }
    }

    public void Delete(string path)
    {
        var normalizedPath = NormalizePath(path);

        if (File.Exists(normalizedPath))
        {
            File.Delete(normalizedPath);
            return;
        }

        if (Directory.Exists(normalizedPath))
        {
            Directory.Delete(normalizedPath, recursive: true);
        }
    }

    public long GetAvailableFreeSpace(string path)
    {
        var normalizedPath = NormalizePath(path);
        var root = Path.GetPathRoot(normalizedPath)
            ?? throw new ArgumentException($"Unable to determine drive root for '{normalizedPath}'.", nameof(path));

        var driveInfo = new DriveInfo(root);
        return driveInfo.AvailableFreeSpace;
    }

    public bool SupportsSparsePreallocation(string path)
    {
        var normalizedPath = NormalizePath(path);
        var root = Path.GetPathRoot(normalizedPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return false;
            }

            var format = drive.DriveFormat;
            return format.Equals("NTFS", StringComparison.OrdinalIgnoreCase)
                || format.Equals("ReFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (DriveNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public bool IsSameVolume(string pathA, string pathB)
    {
        var rootA = Path.GetPathRoot(NormalizePath(pathA));
        var rootB = Path.GetPathRoot(NormalizePath(pathB));

        return string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
    }

    public void ValidatePathWithinRoot(string path, string rootPath)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(rootPath);

        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedRoot += Path.DirectorySeparatorChar;
        }

        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new PermissionDeniedException(
                $"Path '{normalizedPath}' is outside the allowed destination root '{normalizedRoot}'.");
        }
    }

    private static string NormalizePath(string path) => PathNormalization.NormalizeAbsolutePath(path);

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
