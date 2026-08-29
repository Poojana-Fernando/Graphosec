using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Diagnostics;
using ResumableCopy.Application.Models;
using ResumableCopy.Core.Abstractions;

namespace ResumableCopy.Application.Services;

public sealed class PathAnalysisService : IPathAnalysisService
{
    private readonly IFileSystemService _fileSystemService;
    private readonly IDriveProvider _driveProvider;
    private readonly ILogger<PathAnalysisService> _logger;

    public PathAnalysisService(
        IFileSystemService fileSystemService,
        IDriveProvider driveProvider,
        ILogger<PathAnalysisService>? logger = null)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _driveProvider = driveProvider ?? throw new ArgumentNullException(nameof(driveProvider));
        _logger = logger ?? NullLogger<PathAnalysisService>.Instance;
    }

    public Task<PathAnalysis> AnalyzeSourceAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timer = OperationTimer.Measure(_logger, "SourcePathAnalysis");
        return Task.FromResult(AnalyzeSource(path, cancellationToken));
    }

    public Task<PathAnalysis> AnalyzeDestinationAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timer = OperationTimer.Measure(_logger, "DestinationPathAnalysis");
        return Task.FromResult(AnalyzeDestination(path, cancellationToken));
    }

    private PathAnalysis AnalyzeSource(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathAnalysis.Empty(path);
        }

        if (!TryNormalize(path, out var normalized, out var error))
        {
            return PathAnalysis.Failed(path, error);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var volumeReady = ProbeVolume(normalized);
        if (!volumeReady)
        {
            return new PathAnalysis(normalized, Exists: false, VolumeReady: false, SizeBytes: 0, AvailableFreeSpace: null, "Volume is not ready.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!_fileSystemService.FileExists(normalized))
            {
                return new PathAnalysis(normalized, Exists: false, VolumeReady: true, SizeBytes: 0, AvailableFreeSpace: null, "Source file not found.");
            }

            var metadata = _fileSystemService.GetMetadata(normalized);
            return new PathAnalysis(normalized, Exists: true, VolumeReady: true, metadata.Length, AvailableFreeSpace: null, ErrorMessage: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return PathAnalysis.Failed(normalized, exception.Message);
        }
    }

    private PathAnalysis AnalyzeDestination(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathAnalysis.Empty(path);
        }

        if (!TryNormalize(path, out var normalized, out var error))
        {
            return PathAnalysis.Failed(path, error);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var volumeReady = ProbeVolume(normalized);
        if (!volumeReady)
        {
            return new PathAnalysis(normalized, Exists: false, VolumeReady: false, SizeBytes: 0, AvailableFreeSpace: null, "Volume is not ready.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var exists = _fileSystemService.FileExists(normalized);
            long? freeSpace = null;
            try
            {
                using var freeSpaceTimer = OperationTimer.Measure(_logger, "FreeSpaceQuery");
                freeSpace = _fileSystemService.GetAvailableFreeSpace(normalized);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DriveNotFoundException)
            {
                return new PathAnalysis(
                    normalized,
                    exists,
                    VolumeReady: false,
                    SizeBytes: 0,
                    AvailableFreeSpace: null,
                    "Unable to query destination free space.");
            }

            return new PathAnalysis(normalized, exists, VolumeReady: true, SizeBytes: 0, freeSpace, ErrorMessage: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return PathAnalysis.Failed(normalized, exception.Message);
        }
    }

    private bool ProbeVolume(string path)
    {
        using var timer = OperationTimer.Measure(_logger, "DriveReadinessQuery");
        try
        {
            _driveProvider.InvalidateReadinessCache(path);
            return _driveProvider.IsVolumeReady(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DriveNotFoundException)
        {
            return false;
        }
    }

    private static bool TryNormalize(string path, out string normalized, out string error)
    {
        normalized = path;
        error = string.Empty;

        if (!Path.IsPathRooted(path))
        {
            error = "Path must be absolute.";
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = exception.Message;
            return false;
        }
    }
}
