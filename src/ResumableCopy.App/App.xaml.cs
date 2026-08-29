using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResumableCopy.App.Diagnostics;
using ResumableCopy.App.Devices;
using ResumableCopy.App.Logging;
using ResumableCopy.App.Services;
using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Configuration;
using ResumableCopy.Application.DependencyInjection;
using ResumableCopy.Application.ViewModels;
using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.App;

public partial class App : System.Windows.Application
{
    private DispatcherResponsivenessMonitor? _responsivenessMonitor;

    private IVolumeChangeMonitor? _volumeChangeMonitor;

    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        var settings = configuration.GetSection(ResumableCopySettings.SectionName).Get<ResumableCopySettings>()
            ?? new ResumableCopySettings();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddResumableCopyApplication(configuration);
        services.AddResumableCopyLogging(ParseLogLevel(settings.Logging.MinimumLevel));
        services.AddSingleton<IUiThread, WpfUiThread>();
        services.AddSingleton<IFilePickerService, WpfFilePickerService>();
        services.AddSingleton<IVolumeChangeMonitor, WindowsVolumeChangeMonitor>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<IThemeService, WpfThemeService>();
        services.AddSingleton<IUserPromptService, WpfUserPromptService>();

        var logDirectory = Environment.ExpandEnvironmentVariables(settings.Logging.LogDirectory);
        services.AddSingleton<ILoggerProvider>(_ =>
            new FileLoggerProvider(logDirectory, ParseLogLevel(settings.Logging.MinimumLevel)));

        Services = services.BuildServiceProvider();

        var themeService = Services.GetRequiredService<IThemeService>();
        themeService.ApplyTheme(themeService.CurrentTheme);

        var logger = Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation(
            "Starting {ProductName} {Version} on {OperatingSystem} ({Framework})",
            ApplicationInfo.ProductName,
            ApplicationInfo.Version,
            ApplicationInfo.OperatingSystemDescription,
            ApplicationInfo.FrameworkDescription);

        GlobalExceptionHandler.Register(this, logger);

        StartVolumeMonitoring(logger);

        if (settings.Diagnostics.MonitorUiResponsiveness)
        {
            var thresholdMs = settings.Diagnostics.UiStallThresholdMilliseconds > 0
                ? settings.Diagnostics.UiStallThresholdMilliseconds
                : 500;
            _responsivenessMonitor = new DispatcherResponsivenessMonitor(
                Dispatcher,
                logger,
                TimeSpan.FromMilliseconds(thresholdMs));
        }

        var mainWindow = new MainWindow
        {
            DataContext = Services.GetRequiredService<MainViewModel>()
        };

        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _responsivenessMonitor?.Dispose();
        _volumeChangeMonitor?.Dispose();
        if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }

    private void StartVolumeMonitoring(ILogger logger)
    {
        _volumeChangeMonitor = Services.GetRequiredService<IVolumeChangeMonitor>();
        var orchestrator = Services.GetRequiredService<ITransferOrchestrator>();
        var driveProvider = Services.GetRequiredService<IDriveProvider>();
        var diskSpaceManager = Services.GetRequiredService<IDiskSpaceManager>();

        _volumeChangeMonitor.VolumesChanged += (_, _) =>
        {
            logger.LogDebug("Volume change detected. Refreshing drive readiness state.");
            driveProvider.InvalidateReadinessCache();
            diskSpaceManager.InvalidateCache();
            orchestrator.NotifyVolumesChanged();
        };

        _volumeChangeMonitor.Start();
    }

    private static LogLevel ParseLogLevel(string? value) =>
        Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level) ? level : LogLevel.Information;
}
