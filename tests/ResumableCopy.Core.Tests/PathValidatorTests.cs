using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Security;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class PathValidatorTests
{
    private readonly PathValidator _validator = new();

    [Fact]
    public void ValidateCopyPaths_RejectsRelativeSource()
    {
        var exception = Assert.Throws<InvalidPathException>(() =>
            _validator.ValidateCopyPaths("relative.bin", @"C:\temp\dest.bin"));

        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCopyPaths_RejectsSameSourceAndDestination()
    {
        using var temp = new TempDirectory();
        var path = temp.GetPath("same.bin");

        var exception = Assert.Throws<InvalidPathException>(() =>
            _validator.ValidateCopyPaths(path, path));

        Assert.Contains("same", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCopyPaths_RejectsDestinationInsideSourcePath()
    {
        using var temp = new TempDirectory();
        var sourcePath = temp.GetPath("folder", "source.bin");
        var destinationPath = temp.GetPath("folder", "source.bin", "nested", "dest.bin");

        var exception = Assert.Throws<InvalidPathException>(() =>
            _validator.ValidateCopyPaths(sourcePath, destinationPath));

        Assert.Contains("overlap", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCopyPaths_RejectsReservedDeviceName()
    {
        using var temp = new TempDirectory();
        var sourcePath = temp.GetPath("valid.bin");
        var destinationPath = temp.GetPath("CON");

        var exception = Assert.Throws<InvalidPathException>(() =>
            _validator.ValidateCopyPaths(sourcePath, destinationPath));

        Assert.Contains("reserved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCopyPaths_AcceptsValidPaths()
    {
        using var temp = new TempDirectory();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "target.bin");

        var (normalizedSource, normalizedDestination) =
            _validator.ValidateCopyPaths(sourcePath, destinationPath);

        Assert.Equal(Path.GetFullPath(sourcePath), normalizedSource);
        Assert.Equal(Path.GetFullPath(destinationPath), normalizedDestination);
    }
}
