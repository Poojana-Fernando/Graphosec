using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Configuration;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Storage;

namespace ResumableCopy.Core.Tests;

public class DiskSpaceManagerCacheTests
{
    [Fact]
    public void GetAvailableFreeSpace_WhenVolumeNotReady_ThrowsDestinationUnavailable()
    {
        var fileSystem = new CountingFileSystem { FreeSpace = 1024 };
        var drive = new StubDriveProvider { Ready = false };
        var manager = new DiskSpaceManager(fileSystem, drive, new DeviceProbeOptions
        {
            ReadinessCacheTtl = TimeSpan.FromSeconds(30)
        });

        var exception = Assert.Throws<DestinationUnavailableException>(() =>
            manager.GetAvailableFreeSpace(@"Z:\dest.bin"));

        Assert.Contains("not ready", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fileSystem.FreeSpaceCalls);
    }

    [Fact]
    public void GetAvailableFreeSpace_CachesWithinTtl()
    {
        var fileSystem = new CountingFileSystem { FreeSpace = 2048 };
        var drive = new StubDriveProvider { Ready = true };
        var manager = new DiskSpaceManager(fileSystem, drive, new DeviceProbeOptions
        {
            ReadinessCacheTtl = TimeSpan.FromSeconds(30)
        });

        Assert.Equal(2048, manager.GetAvailableFreeSpace(@"C:\a.bin"));
        fileSystem.FreeSpace = 1;
        Assert.Equal(2048, manager.GetAvailableFreeSpace(@"C:\b.bin"));
        Assert.Equal(1, fileSystem.FreeSpaceCalls);
    }

    [Fact]
    public void GetAvailableFreeSpace_WithoutProbeOptions_DoesNotCache()
    {
        var fileSystem = new CountingFileSystem { FreeSpace = 2048 };
        var manager = new DiskSpaceManager(fileSystem);

        Assert.Equal(2048, manager.GetAvailableFreeSpace(@"C:\a.bin"));
        fileSystem.FreeSpace = 4;
        Assert.Equal(4, manager.GetAvailableFreeSpace(@"C:\a.bin"));
        Assert.Equal(2, fileSystem.FreeSpaceCalls);
    }

    [Fact]
    public void GetAvailableFreeSpace_WhenQueryThrows_WrapsAsDestinationUnavailable()
    {
        var fileSystem = new CountingFileSystem { ThrowOnFreeSpace = true };
        var drive = new StubDriveProvider { Ready = true };
        var manager = new DiskSpaceManager(fileSystem, drive);

        Assert.Throws<DestinationUnavailableException>(() => manager.GetAvailableFreeSpace(@"C:\a.bin"));
    }

    private sealed class StubDriveProvider : IDriveProvider
    {
        public bool Ready { get; set; } = true;

        public string? GetVolumeRoot(string path) => Path.GetPathRoot(Path.GetFullPath(path));

        public bool IsVolumeReady(string path) => Ready;

        public void InvalidateReadinessCache(string? path = null)
        {
        }
    }

    private sealed class CountingFileSystem : IFileSystemService
    {
        public long FreeSpace { get; set; }

        public int FreeSpaceCalls { get; private set; }

        public bool ThrowOnFreeSpace { get; set; }

        public bool FileExists(string path) => true;

        public FileMetadata GetMetadata(string path) =>
            new(0, DateTime.UtcNow, DateTime.UtcNow, FileAttributes.Normal);

        public Stream OpenRead(string path, int ioBufferSize = CopyOptions.DefaultIoBufferSize) =>
            throw new NotSupportedException();

        public Stream OpenWrite(string path, bool createNew, int ioBufferSize = CopyOptions.DefaultIoBufferSize) =>
            throw new NotSupportedException();

        public Stream OpenReadWrite(string path, bool createNew, FileShare share, int ioBufferSize = CopyOptions.DefaultIoBufferSize) =>
            throw new NotSupportedException();

        public void EnsureDirectory(string path)
        {
        }

        public void ReplaceOrMove(string sourcePath, string destinationPath, bool overwrite)
        {
        }

        public void Delete(string path)
        {
        }

        public long GetAvailableFreeSpace(string path)
        {
            FreeSpaceCalls++;
            if (ThrowOnFreeSpace)
            {
                throw new IOException("drive removed");
            }

            return FreeSpace;
        }

        public bool SupportsSparsePreallocation(string path) => true;

        public bool IsSameVolume(string pathA, string pathB) => true;

        public void ValidatePathWithinRoot(string path, string rootPath)
        {
        }
    }
}
