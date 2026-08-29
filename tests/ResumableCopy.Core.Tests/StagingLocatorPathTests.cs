using ResumableCopy.Core.Storage;

namespace ResumableCopy.Core.Tests;

public class StagingLocatorPathTests
{
    [Fact]
    public void GetCacheDirectory_ForFileAtDriveRoot_UsesDriveRootCopyCache()
    {
        var locator = new StagingLocator();

        var cacheDirectory = locator.GetCacheDirectory(@"D:\Lightyear-Frontier-AnkerGames.zip");

        Assert.Equal(@"D:\.copycache", cacheDirectory);
    }

    [Fact]
    public void GetCacheDirectory_ForDriveRootPath_UsesDriveRootCopyCache()
    {
        var locator = new StagingLocator();

        var cacheDirectory = locator.GetCacheDirectory(@"D:\");

        Assert.Equal(@"D:\.copycache", cacheDirectory);
    }

    [Fact]
    public void GetCacheDirectory_ForNestedDestination_UsesParentDirectoryCopyCache()
    {
        var locator = new StagingLocator();

        var cacheDirectory = locator.GetCacheDirectory(@"C:\data\dest\file.bin");

        Assert.Equal(@"C:\data\dest\.copycache", cacheDirectory);
    }
}
