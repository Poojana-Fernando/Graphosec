using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Configuration;
using ResumableCopy.Core.Concurrency;
using ResumableCopy.Core.Core;
using ResumableCopy.Core.Devices;
using ResumableCopy.Core.Integrity;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Performance;
using ResumableCopy.Core.Reliability;
using ResumableCopy.Core.Resume;
using ResumableCopy.Core.Security;
using ResumableCopy.Core.Storage;

namespace ResumableCopy.Core.DependencyInjection;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddResumableCopyCore(this IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(StagingOptions)))
        {
            services.AddSingleton(new StagingOptions());
        }

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(DeviceProbeOptions)))
        {
            services.AddSingleton(new DeviceProbeOptions());
        }

        services.AddSingleton<IStagingLocator>(provider => new StagingLocator(provider.GetRequiredService<StagingOptions>()));
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IHashService, Sha256HashService>();
        services.AddSingleton<ISourceIdentityProvider, SourceIdentityProvider>();
        services.AddSingleton<IChunkVerifier, ChunkVerifier>();
        services.AddSingleton<IFileVerifier, FileVerifier>();
        services.AddSingleton<IPathValidator, PathValidator>();
        services.AddSingleton<ICopyPerformanceAdvisor, CopyPerformanceAdvisor>();
        services.AddSingleton<DriveProvider>();
        services.AddSingleton<IDriveProvider>(provider => new CachedDriveProvider(
            provider.GetRequiredService<DriveProvider>(),
            provider.GetRequiredService<DeviceProbeOptions>()));
        services.AddSingleton<IDiskSpaceManager>(provider => new DiskSpaceManager(
            provider.GetRequiredService<IFileSystemService>(),
            provider.GetRequiredService<IDriveProvider>(),
            provider.GetRequiredService<DeviceProbeOptions>()));
        services.AddSingleton<ISessionRepositoryProvider, SqliteSessionRepositoryProvider>();
        services.AddSingleton<IStagingChunkValidator, StagingChunkValidator>();
        services.AddSingleton<IDeviceMonitor, StorageDeviceMonitor>();
        services.AddSingleton<ITransferEnvironmentMonitor, TransferEnvironmentMonitor>();
        services.AddSingleton<ITransferRecoveryService, TransferRecoveryService>();
        services.AddSingleton<ISessionCleanupService, SessionCleanupService>();
        services.AddSingleton<IFaultInjector>(_ => NullFaultInjector.Instance);
        services.AddSingleton<IChunkCopyExecutor>(provider => new ParallelChunkCopyExecutor(
            provider.GetRequiredService<IFileSystemService>(),
            provider.GetRequiredService<IHashService>(),
            provider.GetRequiredService<IChunkVerifier>(),
            provider.GetRequiredService<ITransferEnvironmentMonitor>(),
            provider.GetRequiredService<IFaultInjector>(),
            provider.GetService<ILogger<ParallelChunkCopyExecutor>>()));
        services.AddSingleton<ICopyEngine>(provider => new CopyEngine(
            provider.GetRequiredService<IFileSystemService>(),
            provider.GetRequiredService<ISourceIdentityProvider>(),
            provider.GetRequiredService<IStagingLocator>(),
            provider.GetRequiredService<IHashService>(),
            provider.GetRequiredService<IChunkVerifier>(),
            provider.GetRequiredService<IFileVerifier>(),
            provider.GetRequiredService<ISessionRepositoryProvider>(),
            provider.GetRequiredService<IDiskSpaceManager>(),
            provider.GetRequiredService<ITransferRecoveryService>(),
            provider.GetRequiredService<ITransferEnvironmentMonitor>(),
            provider.GetRequiredService<IChunkCopyExecutor>(),
            provider.GetRequiredService<IFaultInjector>(),
            provider.GetRequiredService<IPathValidator>(),
            provider.GetRequiredService<ICopyPerformanceAdvisor>(),
            provider.GetRequiredService<ISessionCleanupService>(),
            provider.GetService<ILogger<CopyEngine>>()));

        return services;
    }
}
