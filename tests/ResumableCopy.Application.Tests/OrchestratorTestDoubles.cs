using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Resume;

namespace ResumableCopy.Application.Tests;

internal sealed class NoOpRecoveryService : ITransferRecoveryService
{
    public Func<string, string, RecoveryResult>? RecoverSessionHandler { get; init; }

    public Func<string, Exception?>? DiscoverExceptionFactory { get; init; }

    public ValueTask<IReadOnlyList<RecoverableSessionInfo>> DiscoverRecoverableSessionsAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (DiscoverExceptionFactory?.Invoke(destinationPath) is Exception exception)
        {
            throw exception;
        }

        return ValueTask.FromResult<IReadOnlyList<RecoverableSessionInfo>>([]);
    }

    public ValueTask<RecoveryResult> RecoverSessionAsync(
        string destinationPath,
        string sessionId,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            RecoverSessionHandler?.Invoke(destinationPath, sessionId)
            ?? CreateResumableResult(sessionId, destinationPath));

    public ValueTask<IReadOnlyList<RecoveryResult>> RecoverUnfinishedSessionsAsync(
        string destinationPath,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<RecoveryResult>>([]);

    internal static RecoveryResult CreateResumableResult(string sessionId, string destinationPath) =>
        new(
            sessionId,
            CopyState.Paused,
            CanResume: true,
            InvalidatedChunkCount: 0,
            Message: "Session is ready to resume.",
            CreateMinimalSession(sessionId, destinationPath));

    internal static RecoveryResult CreateUnavailableDestinationResult(string sessionId, string destinationPath) =>
        new(
            sessionId,
            CopyState.WaitingForDestination,
            CanResume: false,
            InvalidatedChunkCount: 0,
            Message: "Destination volume is not ready.",
            CreateMinimalSession(sessionId, destinationPath, CopyState.WaitingForDestination));

    private static CopySession CreateMinimalSession(
        string sessionId,
        string destinationPath,
        CopyState state = CopyState.Paused)
    {
        var timestamp = DateTime.UtcNow;
        return new CopySession
        {
            SessionId = sessionId,
            SourcePath = @"C:\source.bin",
            DestinationPath = destinationPath,
            SourceIdentity = new SourceIdentity(1024, timestamp, timestamp),
            StagingPath = Path.Combine(Path.GetDirectoryName(destinationPath)!, ".copycache", $"{sessionId}.part"),
            ChunkSize = 1024,
            TotalChunks = 1,
            State = state,
        };
    }
}

internal sealed class NoOpSessionCleanupService : ISessionCleanupService
{
    public List<string> CleanedSessionIds { get; } = [];

    public ValueTask CleanupSessionAsync(
        string destinationPath,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        CleanedSessionIds.Add(sessionId);
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestDriveProvider : IDriveProvider
{
    public bool Ready { get; set; } = true;

    public string? GetVolumeRoot(string path) => Path.GetPathRoot(path);

    public bool IsVolumeReady(string path) => Ready;

    public void InvalidateReadinessCache(string? path = null)
    {
    }
}

internal sealed class TestDeviceMonitor : IDeviceMonitor
{
    private readonly HashSet<string> _unreadyRoots = new(StringComparer.OrdinalIgnoreCase);

    public void SetVolumeReady(string path, bool isReady)
    {
        var root = GetVolumeRoot(path);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        if (isReady)
        {
            _unreadyRoots.Remove(root);
        }
        else
        {
            _unreadyRoots.Add(root);
        }
    }

    public bool IsPathAccessible(string path) => IsVolumeReady(path);

    public bool IsVolumeReady(string path)
    {
        var root = GetVolumeRoot(path);
        return string.IsNullOrWhiteSpace(root) || !_unreadyRoots.Contains(root);
    }

    public string? GetVolumeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        if (fullPath.Length >= 2 && fullPath[1] == ':')
        {
            return fullPath[..2] + Path.DirectorySeparatorChar;
        }

        return null;
    }
}

internal sealed class TestFileSystemService : IFileSystemService
{
    private readonly FileSystemService _inner = new();
    private readonly HashSet<string> _existingFiles = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path) => _existingFiles.Add(Path.GetFullPath(path));

    public bool FileExists(string path) => _existingFiles.Contains(Path.GetFullPath(path));

    public FileMetadata GetMetadata(string path) => _inner.GetMetadata(path);

    public Stream OpenRead(string path, int ioBufferSize = CopyOptions.DefaultIoBufferSize) =>
        _inner.OpenRead(path, ioBufferSize);

    public Stream OpenWrite(string path, bool createNew, int ioBufferSize = CopyOptions.DefaultIoBufferSize) =>
        _inner.OpenWrite(path, createNew, ioBufferSize);

    public Stream OpenReadWrite(string path, bool createNew, FileShare share, int ioBufferSize = CopyOptions.DefaultIoBufferSize) =>
        _inner.OpenReadWrite(path, createNew, share, ioBufferSize);

    public void EnsureDirectory(string path) => _inner.EnsureDirectory(path);

    public void Delete(string path) => _inner.Delete(path);

    public long GetAvailableFreeSpace(string path) => _inner.GetAvailableFreeSpace(path);

    public bool SupportsSparsePreallocation(string path) => _inner.SupportsSparsePreallocation(path);

    public bool IsSameVolume(string pathA, string pathB) => _inner.IsSameVolume(pathA, pathB);

    public void ValidatePathWithinRoot(string path, string rootPath) =>
        _inner.ValidatePathWithinRoot(path, rootPath);

    public void ReplaceOrMove(string sourcePath, string destinationPath, bool overwriteExisting) =>
        _inner.ReplaceOrMove(sourcePath, destinationPath, overwriteExisting);
}
