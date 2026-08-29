using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Security;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class SecurityCopyEngineTests
{
    [Fact]
    public async Task CopyAsync_SameSourceAndDestination_ThrowsInvalidPath()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var path = temp.GetPath("same.bin");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);

        await Assert.ThrowsAsync<InvalidPathException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(path, path),
                progress: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task CopyAsync_DestinationInsideSourceTree_ThrowsInvalidPath()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourcePath = temp.GetPath("tree", "source.bin");
        var destinationPath = temp.GetPath("tree", "source.bin", "nested", "dest.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);

        await Assert.ThrowsAsync<InvalidPathException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath),
                progress: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task CopyAsync_ReservedDestinationName_ThrowsInvalidPath()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("COM1");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);

        await Assert.ThrowsAsync<InvalidPathException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath),
                progress: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task CopyAsync_SourceSymlink_ThrowsPermissionDeniedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var targetPath = temp.GetPath("target.bin");
        var linkPath = temp.GetPath("link.bin");
        var destinationPath = temp.GetPath("dest", "out.bin");
        await File.WriteAllBytesAsync(targetPath, [1, 2, 3]);

        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (IOException)
        {
            return;
        }

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(linkPath, destinationPath),
                progress: null,
                CancellationToken.None));
    }

    [Fact]
    public void SourceIdentityProvider_CapturesFileIdentityOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var fileSystemService = new FileSystemService();
        var provider = new SourceIdentityProvider(fileSystemService);
        var path = temp.GetPath("identity.bin");
        File.WriteAllBytes(path, [7, 8, 9]);

        var identity = provider.Capture(path);

        Assert.NotNull(identity.VolumeSerial);
        Assert.NotNull(identity.FileId);
    }
}
