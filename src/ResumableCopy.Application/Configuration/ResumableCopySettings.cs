using ResumableCopy.Core.Domain;

namespace ResumableCopy.Application.Configuration;

public sealed class ResumableCopySettings
{
    public const string SectionName = "ResumableCopy";

    public LoggingSettings Logging { get; set; } = new();

    public CopySettings Copy { get; set; } = new();

    public DiagnosticsSettings Diagnostics { get; set; } = new();

    public StagingSettings Staging { get; set; } = new();
}

public sealed class LoggingSettings
{
    public string MinimumLevel { get; set; } = "Information";

    public string LogDirectory { get; set; } = "%LOCALAPPDATA%\\ResumableCopy\\logs";
}

public sealed class CopySettings
{
    public int ChunkSize { get; set; }

    public int MaximumWorkers { get; set; }

    public int MaximumQueuedChunks { get; set; }

    public int MaximumChunkAttempts { get; set; }

    public int RetryDelayMilliseconds { get; set; }

    public int IoBufferSize { get; set; }

    public bool UseAdaptivePerformance { get; set; } = true;

    public bool VerifyWholeFileAfterCopy { get; set; } = true;

    public bool FlushEveryChunk { get; set; } = true;

    public long FlushIntervalBytes { get; set; }

    public CopyOptions ToCopyOptions(bool overwriteExisting)
    {
        return new CopyOptions
        {
            OverwriteExisting = overwriteExisting,
            UseAdaptivePerformance = UseAdaptivePerformance,
            VerifyWholeFileAfterCopy = VerifyWholeFileAfterCopy,
            FlushEveryChunk = FlushEveryChunk,
            FlushIntervalBytes = FlushIntervalBytes > 0 ? FlushIntervalBytes : CopyOptions.DefaultFlushIntervalBytes,
            ChunkSize = ChunkSize > 0 ? ChunkSize : CopyOptions.DefaultChunkSize,
            MaximumWorkers = MaximumWorkers > 0 ? MaximumWorkers : CopyOptions.DefaultMaximumWorkers,
            MaximumQueuedChunks = MaximumQueuedChunks > 0 ? MaximumQueuedChunks : CopyOptions.DefaultMaximumQueuedChunks,
            MaximumChunkAttempts = MaximumChunkAttempts > 0 ? MaximumChunkAttempts : CopyOptions.DefaultMaximumChunkAttempts,
            RetryDelayMilliseconds = RetryDelayMilliseconds >= 0 ? RetryDelayMilliseconds : CopyOptions.DefaultRetryDelayMilliseconds,
            IoBufferSize = IoBufferSize > 0 ? IoBufferSize : CopyOptions.DefaultIoBufferSize
        };
    }
}

public sealed class DiagnosticsSettings
{
    public string Level { get; set; } = "Normal";

    public bool MonitorUiResponsiveness { get; set; }

    public int UiStallThresholdMilliseconds { get; set; } = 500;

    public int ProgressUpdateIntervalMilliseconds { get; set; } = 200;

    public int DeviceProbeCacheMilliseconds { get; set; } = 2000;

    public int ReconnectProbeIntervalMilliseconds { get; set; } = 3000;
}

public sealed class StagingSettings
{
    public string CacheDirectoryName { get; set; } = ".copycache";
}
