using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Core;
using ResumableCopy.Core.Devices;
using ResumableCopy.Core.Integrity;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Resume;
using ResumableCopy.Core.Storage;

namespace ResumableCopy.Core.Tests.TestSupport;

public sealed class SqliteCopyEngineTestContext
{
    public SqliteCopyEngineTestContext()
    {
        FileSystemService = new FileSystemService();
        HashService = new Sha256HashService();
        SourceIdentity = new SourceIdentityProvider(FileSystemService);
        StagingLocator = new StagingLocator();
        SessionRepositoryProvider = new SqliteSessionRepositoryProvider(StagingLocator);
        DiskSpaceManager = new DiskSpaceManager(FileSystemService);
        DeviceMonitor = new StorageDeviceMonitor(FileSystemService, new DriveProvider());
        Engine = CopyEngineTestFactory.Create(FileSystemService, SessionRepositoryProvider, DeviceMonitor);
        RecoveryService = CreateRecoveryService();
    }

    private TransferRecoveryService CreateRecoveryService()
    {
        var chunkVerifier = new ChunkVerifier(HashService);
        return new TransferRecoveryService(
            SessionRepositoryProvider,
            FileSystemService,
            SourceIdentity,
            new StagingChunkValidator(FileSystemService, chunkVerifier),
            DeviceMonitor,
            StagingLocator);
    }

    public FileSystemService FileSystemService { get; }

    public Sha256HashService HashService { get; }

    public SourceIdentityProvider SourceIdentity { get; }

    public StagingLocator StagingLocator { get; }

    public SqliteSessionRepositoryProvider SessionRepositoryProvider { get; }

    public DiskSpaceManager DiskSpaceManager { get; }

    public IDeviceMonitor DeviceMonitor { get; }

    public TransferRecoveryService RecoveryService { get; }

    public CopyEngine Engine { get; }

    public ISessionRepository GetRepository(string destinationPath) =>
        SessionRepositoryProvider.GetRepository(destinationPath);
}
