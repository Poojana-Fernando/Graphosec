using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Configuration;
using ResumableCopy.Core.Devices;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class TransferEnvironmentMonitorCacheTests
{
    [Fact]
    public void EnsureSourceIdentityUnchanged_WithTtl_CapturesOnceWithinWindow()
    {
        using var temp = new TempDirectory();
        var sourcePath = temp.GetPath("source.bin");
        File.WriteAllBytes(sourcePath, [1, 2, 3, 4]);

        var fileSystem = new FileSystemService();
        var identity = new CountingSourceIdentityProvider(fileSystem);
        var monitor = new TransferEnvironmentMonitor(
            fileSystem,
            new StorageDeviceMonitor(fileSystem, new DriveProvider()),
            new DiskSpaceManager(fileSystem),
            identity,
            new StagingLocator(),
            new DeviceProbeOptions { ReadinessCacheTtl = TimeSpan.FromSeconds(30) });

        var captured = identity.Capture(sourcePath);
        identity.Reset();

        var session = new CopySession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            SourcePath = sourcePath,
            DestinationPath = temp.GetPath("dest.bin"),
            StagingPath = temp.GetPath("staging.bin"),
            SourceIdentity = captured,
            ChunkSize = 4,
            TotalChunks = 1,
            Chunks =
            [
                new ChunkRecord { Index = 0, Offset = 0, Length = 4 }
            ]
        };

        monitor.EnsureSourceIdentityUnchanged(session);
        monitor.EnsureSourceIdentityUnchanged(session);

        Assert.Equal(1, identity.CaptureCount);
    }

    [Fact]
    public void EnsureSourceIdentityUnchanged_WithoutTtl_CapturesEveryCall()
    {
        using var temp = new TempDirectory();
        var sourcePath = temp.GetPath("source.bin");
        File.WriteAllBytes(sourcePath, [1, 2, 3, 4]);

        var fileSystem = new FileSystemService();
        var identity = new CountingSourceIdentityProvider(fileSystem);
        var monitor = new TransferEnvironmentMonitor(
            fileSystem,
            new StorageDeviceMonitor(fileSystem, new DriveProvider()),
            new DiskSpaceManager(fileSystem),
            identity,
            new StagingLocator());

        var captured = identity.Capture(sourcePath);
        identity.Reset();

        var session = new CopySession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            SourcePath = sourcePath,
            DestinationPath = temp.GetPath("dest.bin"),
            StagingPath = temp.GetPath("staging.bin"),
            SourceIdentity = captured,
            ChunkSize = 4,
            TotalChunks = 1
        };

        monitor.EnsureSourceIdentityUnchanged(session);
        monitor.EnsureSourceIdentityUnchanged(session);

        Assert.Equal(2, identity.CaptureCount);
    }

    private sealed class CountingSourceIdentityProvider : ISourceIdentityProvider
    {
        private readonly SourceIdentityProvider _inner;

        public CountingSourceIdentityProvider(IFileSystemService fileSystemService)
        {
            _inner = new SourceIdentityProvider(fileSystemService);
        }

        public int CaptureCount { get; private set; }

        public void Reset() => CaptureCount = 0;

        public SourceIdentity Capture(string path)
        {
            CaptureCount++;
            return _inner.Capture(path);
        }
    }
}
