using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Concurrency;
using ResumableCopy.Core.Core;
using ResumableCopy.Core.Devices;
using ResumableCopy.Core.Integrity;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Reliability;
using ResumableCopy.Core.Resume;
using ResumableCopy.Core.Storage;

namespace ResumableCopy.Core.Tests.TestSupport;

public sealed class FaultInjectionTestContext
{
    public FaultInjectionTestContext(params FaultRule[] rules)
        : this(new DeterministicFaultInjector(rules))
    {
    }

    public FaultInjectionTestContext(DeterministicFaultInjector faultInjector)
    {
        FaultInjector = faultInjector;
        FileSystemService = new FileSystemService();
        HashService = new Sha256HashService();
        SourceIdentityProvider = new SourceIdentityProvider(FileSystemService);
        StagingLocator = new StagingLocator();
        SessionRepositoryProvider = new SqliteSessionRepositoryProvider(
            StagingLocator,
            new Storage.Sqlite.SqliteSessionRepositoryOptions { FaultInjector = faultInjector });
        DiskSpaceManager = new DiskSpaceManager(FileSystemService);
        DeviceMonitor = new StorageDeviceMonitor(FileSystemService, new DriveProvider());
        EnvironmentMonitor = new TransferEnvironmentMonitor(
            FileSystemService,
            DeviceMonitor,
            DiskSpaceManager,
            SourceIdentityProvider,
            StagingLocator);
        var chunkVerifier = new ChunkVerifier(HashService);
        RecoveryService = new TransferRecoveryService(
            SessionRepositoryProvider,
            FileSystemService,
            SourceIdentityProvider,
            new StagingChunkValidator(FileSystemService, chunkVerifier),
            DeviceMonitor,
            StagingLocator);
        ChunkCopyExecutor = new ParallelChunkCopyExecutor(
            FileSystemService,
            HashService,
            chunkVerifier,
            EnvironmentMonitor,
            faultInjector);

        Engine = new CopyEngine(
            FileSystemService,
            SourceIdentityProvider,
            StagingLocator,
            HashService,
            chunkVerifier,
            new FileVerifier(HashService),
            SessionRepositoryProvider,
            DiskSpaceManager,
            RecoveryService,
            EnvironmentMonitor,
            ChunkCopyExecutor,
            faultInjector);
    }

    public DeterministicFaultInjector FaultInjector { get; }

    public FileSystemService FileSystemService { get; }

    public Sha256HashService HashService { get; }

    public SourceIdentityProvider SourceIdentityProvider { get; }

    public StagingLocator StagingLocator { get; }

    public SqliteSessionRepositoryProvider SessionRepositoryProvider { get; }

    public DiskSpaceManager DiskSpaceManager { get; }

    public IDeviceMonitor DeviceMonitor { get; }

    public ITransferEnvironmentMonitor EnvironmentMonitor { get; }

    public TransferRecoveryService RecoveryService { get; }

    public ParallelChunkCopyExecutor ChunkCopyExecutor { get; }

    public CopyEngine Engine { get; }
}
