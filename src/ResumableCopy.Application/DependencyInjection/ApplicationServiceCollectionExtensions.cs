using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Configuration;
using ResumableCopy.Application.Devices;
using ResumableCopy.Application.Services;
using ResumableCopy.Application.ViewModels;
using ResumableCopy.Core.Configuration;
using ResumableCopy.Core.DependencyInjection;

namespace ResumableCopy.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddResumableCopyApplication(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is null)
        {
            services.Configure<ResumableCopySettings>(_ => { });
        }
        else
        {
            services.Configure<ResumableCopySettings>(configuration.GetSection(ResumableCopySettings.SectionName));
        }

        services.AddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ResumableCopySettings>>().Value;
            return new StagingOptions
            {
                CacheDirectoryName = settings.Staging.CacheDirectoryName
            };
        });

        services.AddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ResumableCopySettings>>().Value;
            var cacheMs = settings.Diagnostics.DeviceProbeCacheMilliseconds;
            return new DeviceProbeOptions
            {
                ReadinessCacheTtl = TimeSpan.FromMilliseconds(cacheMs > 0 ? cacheMs : 2000)
            };
        });

        services.AddResumableCopyCore();
        services.AddSingleton<IBackgroundExecutor, ThreadPoolBackgroundExecutor>();
        services.AddSingleton<IPathAnalysisService, PathAnalysisService>();
        services.AddSingleton<IDriveEnumerationService, DriveEnumerationService>();
        services.AddSingleton<ITransferHistoryStore, JsonTransferHistoryStore>();
        services.AddSingleton<IDestinationRegistry, JsonDestinationRegistry>();
        services.AddSingleton<IVolumeChangeMonitor, NullVolumeChangeMonitor>();
        services.AddSingleton<ITransferOrchestrator, TransferOrchestrator>();
        services.AddSingleton<MainViewModel>();
        return services;
    }

    public static IServiceCollection AddResumableCopyLogging(
        this IServiceCollection services,
        LogLevel minimumLevel = LogLevel.Information)
    {
        services.AddLogging(builder => builder.SetMinimumLevel(minimumLevel));

        return services;
    }
}
