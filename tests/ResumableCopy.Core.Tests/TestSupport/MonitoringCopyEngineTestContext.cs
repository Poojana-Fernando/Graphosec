using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Core;
using ResumableCopy.Core.Devices;
using ResumableCopy.Core.Integrity;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Resume;
using ResumableCopy.Core.Storage;

namespace ResumableCopy.Core.Tests.TestSupport;

public sealed class MonitoringCopyEngineTestContext
{
    public MonitoringCopyEngineTestContext()
    {
        InnerFileSystem = new FileSystemService();
        FileSystem = new FakeFileSystemService(InnerFileSystem);
        StagingLocator = new StagingLocator();
        SessionRepositoryProvider = new SqliteSessionRepositoryProvider(StagingLocator);
        DeviceMonitor = new FakeDeviceMonitor(new StorageDeviceMonitor(FileSystem, new DriveProvider()));
        Engine = CopyEngineTestFactory.Create(FileSystem, SessionRepositoryProvider, DeviceMonitor);
        RecoveryService = CreateRecoveryService();
    }

    public FileSystemService InnerFileSystem { get; }

    public FakeFileSystemService FileSystem { get; }

    public StagingLocator StagingLocator { get; }

    public SqliteSessionRepositoryProvider SessionRepositoryProvider { get; }

    public FakeDeviceMonitor DeviceMonitor { get; }

    public CopyEngine Engine { get; }

    public TransferRecoveryService RecoveryService { get; }

    private TransferRecoveryService CreateRecoveryService()
    {
        var hashService = new Sha256HashService();
        var chunkVerifier = new ChunkVerifier(hashService);
        return new TransferRecoveryService(
            SessionRepositoryProvider,
            FileSystem,
            new SourceIdentityProvider(FileSystem),
            new StagingChunkValidator(FileSystem, chunkVerifier),
            DeviceMonitor,
            StagingLocator);
    }
}
