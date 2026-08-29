using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Core;
using ResumableCopy.Core.Devices;
using ResumableCopy.Core.Integrity;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Resume;
using ResumableCopy.Core.Storage;

namespace ResumableCopy.Core.Tests.TestSupport;

internal static class CopyEngineTestFactory
{
    public static CopyEngine Create(
        IFileSystemService fileSystemService,
        ISessionRepositoryProvider sessionRepositoryProvider,
        IDeviceMonitor? deviceMonitor = null)
    {
        var hashService = new Sha256HashService();
        var sourceIdentityProvider = new SourceIdentityProvider(fileSystemService);
        var stagingLocator = new StagingLocator();
        var chunkVerifier = new ChunkVerifier(hashService);
        var diskSpaceManager = new DiskSpaceManager(fileSystemService);
        var driveProvider = new DriveProvider();
        deviceMonitor ??= new StorageDeviceMonitor(fileSystemService, driveProvider);
        var environmentMonitor = new TransferEnvironmentMonitor(
            fileSystemService,
            deviceMonitor,
            diskSpaceManager,
            sourceIdentityProvider,
            stagingLocator);

        return new CopyEngine(
            fileSystemService,
            sourceIdentityProvider,
            stagingLocator,
            hashService,
            chunkVerifier,
            new FileVerifier(hashService),
            sessionRepositoryProvider,
            diskSpaceManager,
            new TransferRecoveryService(
                sessionRepositoryProvider,
                fileSystemService,
                sourceIdentityProvider,
                new StagingChunkValidator(fileSystemService, chunkVerifier),
                deviceMonitor,
                stagingLocator),
            environmentMonitor);
    }
}
