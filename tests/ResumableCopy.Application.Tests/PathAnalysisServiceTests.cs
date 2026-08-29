using ResumableCopy.Application.Services;
using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Devices;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.IO;

namespace ResumableCopy.Application.Tests;

public class PathAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeSourceAsync_WhenFileExists_ReturnsSize()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "source.bin");
            await File.WriteAllBytesAsync(path, new byte[2048]);

            var service = CreateService();
            var analysis = await service.AnalyzeSourceAsync(path);

            Assert.True(analysis.Exists);
            Assert.True(analysis.VolumeReady);
            Assert.Equal(2048, analysis.SizeBytes);
            Assert.Null(analysis.ErrorMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzeSourceAsync_WhenFileMissing_ReturnsError()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "missing.bin");
            var service = CreateService();
            var analysis = await service.AnalyzeSourceAsync(path);

            Assert.False(analysis.Exists);
            Assert.Contains("not found", analysis.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzeSourceAsync_WhenVolumeNotReady_DoesNotTouchFileSystem()
    {
        var fileSystem = new CountingFileSystem();
        var service = new PathAnalysisService(fileSystem, new StubDriveProvider { Ready = false });

        var analysis = await service.AnalyzeSourceAsync(@"Z:\missing.bin");

        Assert.False(analysis.VolumeReady);
        Assert.Equal(0, fileSystem.ExistsCalls);
        Assert.Contains("not ready", analysis.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnalyzeDestinationAsync_WhenReady_ReturnsFreeSpace()
    {
        var fileSystem = new CountingFileSystem { FreeSpace = 4096 };
        var service = new PathAnalysisService(fileSystem, new StubDriveProvider { Ready = true });

        var analysis = await service.AnalyzeDestinationAsync(@"C:\dest.bin");

        Assert.True(analysis.VolumeReady);
        Assert.Equal(4096, analysis.AvailableFreeSpace);
        Assert.Equal(1, fileSystem.FreeSpaceCalls);
    }

    [Fact]
    public async Task AnalyzeSourceAsync_HonorsCancellation()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.AnalyzeSourceAsync(@"C:\source.bin", cts.Token));
    }

    private static PathAnalysisService CreateService() =>
        new(new FileSystemService(), new DriveProvider());

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ResumableCopyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class StubDriveProvider : IDriveProvider
    {
        public bool Ready { get; set; } = true;

        public string? GetVolumeRoot(string path) => Path.GetPathRoot(path);

        public bool IsVolumeReady(string path) => Ready;

        public void InvalidateReadinessCache(string? path = null)
        {
        }
    }

    private sealed class CountingFileSystem : IFileSystemService
    {
        public int ExistsCalls { get; private set; }

        public int FreeSpaceCalls { get; private set; }

        public long FreeSpace { get; init; } = 1024;

        public bool FileExists(string path)
        {
            ExistsCalls++;
            return false;
        }

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
            return FreeSpace;
        }

        public bool SupportsSparsePreallocation(string path) => true;

        public bool IsSameVolume(string pathA, string pathB) => true;

        public void ValidatePathWithinRoot(string path, string rootPath)
        {
        }
    }
}
