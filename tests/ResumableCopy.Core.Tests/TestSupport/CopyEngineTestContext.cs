using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Core;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Devices;
using ResumableCopy.Core.Integrity;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Resume;
using ResumableCopy.Core.Storage;

namespace ResumableCopy.Core.Tests.TestSupport;

public sealed class CopyEngineTestContext
{
    public CopyEngineTestContext()
    {
        FileSystemService = new FileSystemService();
        HashService = new Sha256HashService();
        SourceIdentityProvider = new SourceIdentityProvider(FileSystemService);
        StagingLocator = new StagingLocator();
        SessionRepositoryProvider = new InMemorySessionRepositoryProvider();
        SessionRepository = SessionRepositoryProvider.Repository;
        DiskSpaceManager = new DiskSpaceManager(FileSystemService);
        DeviceMonitor = new StorageDeviceMonitor(FileSystemService, new DriveProvider());
        EnvironmentMonitor = new TransferEnvironmentMonitor(
            FileSystemService,
            DeviceMonitor,
            DiskSpaceManager,
            SourceIdentityProvider,
            StagingLocator);
        var chunkVerifier = new ChunkVerifier(HashService);
        TransferRecoveryService = new TransferRecoveryService(
            SessionRepositoryProvider,
            FileSystemService,
            SourceIdentityProvider,
            new StagingChunkValidator(FileSystemService, chunkVerifier),
            DeviceMonitor,
            StagingLocator);

        Engine = new CopyEngine(
            FileSystemService,
            SourceIdentityProvider,
            StagingLocator,
            HashService,
            chunkVerifier,
            new FileVerifier(HashService),
            SessionRepositoryProvider,
            DiskSpaceManager,
            TransferRecoveryService,
            EnvironmentMonitor);
    }

    public IDeviceMonitor DeviceMonitor { get; }

    public ITransferEnvironmentMonitor EnvironmentMonitor { get; }

    public TransferRecoveryService TransferRecoveryService { get; }

    public InMemorySessionRepositoryProvider SessionRepositoryProvider { get; }

    public FileSystemService FileSystemService { get; }

    public Sha256HashService HashService { get; }

    public SourceIdentityProvider SourceIdentityProvider { get; }

    public StagingLocator StagingLocator { get; }

    public InMemorySessionRepository SessionRepository { get; }

    public DiskSpaceManager DiskSpaceManager { get; }

    public CopyEngine Engine { get; }
}

public sealed class FakeFileSystemService : IFileSystemService
{
    private readonly IFileSystemService _inner;
    private readonly long? _failWriteAtByteOffset;
    private long? _freeSpaceOverride;
    private readonly HashSet<string> _hiddenPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _readBlockedPaths = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileSystemService(IFileSystemService inner, long? failWriteAtByteOffset = null)
    {
        _inner = inner;
        _failWriteAtByteOffset = failWriteAtByteOffset;
    }

    public void SetFreeSpace(long bytes) => _freeSpaceOverride = bytes;

    public void ClearFreeSpaceOverride() => _freeSpaceOverride = null;

    public void HidePath(string path) => _hiddenPaths.Add(Path.GetFullPath(path));

    public void BlockRead(string path) => _readBlockedPaths.Add(Path.GetFullPath(path));

    public void RestorePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        _hiddenPaths.Remove(fullPath);
        _readBlockedPaths.Remove(fullPath);
    }

    public bool FileExists(string path)
    {
        if (_hiddenPaths.Contains(Path.GetFullPath(path)))
        {
            return false;
        }

        return _inner.FileExists(path);
    }

    public FileMetadata GetMetadata(string path) => _inner.GetMetadata(path);

    public Stream OpenRead(string path, int ioBufferSize = CopyOptions.DefaultIoBufferSize)
    {
        if (_readBlockedPaths.Contains(Path.GetFullPath(path)))
        {
            throw new IOException("Injected read fault.", unchecked((int)0x80070015));
        }

        return _inner.OpenRead(path, ioBufferSize);
    }

    public Stream OpenWrite(string path, bool createNew, int ioBufferSize = CopyOptions.DefaultIoBufferSize)
    {
        var stream = _inner.OpenWrite(path, createNew, ioBufferSize);
        return _failWriteAtByteOffset is null
            ? stream
            : new FaultInjectingWriteStream(stream, _failWriteAtByteOffset.Value);
    }

    public Stream OpenReadWrite(string path, bool createNew, FileShare share, int ioBufferSize = CopyOptions.DefaultIoBufferSize)
    {
        var stream = _inner.OpenReadWrite(path, createNew, share, ioBufferSize);
        return _failWriteAtByteOffset is null
            ? stream
            : new FaultInjectingWriteStream(stream, _failWriteAtByteOffset.Value);
    }

    public void EnsureDirectory(string path) => _inner.EnsureDirectory(path);

    public void ReplaceOrMove(string sourcePath, string destinationPath, bool overwrite) =>
        _inner.ReplaceOrMove(sourcePath, destinationPath, overwrite);

    public void Delete(string path) => _inner.Delete(path);

    public long GetAvailableFreeSpace(string path) =>
        _freeSpaceOverride ?? _inner.GetAvailableFreeSpace(path);

    public bool SupportsSparsePreallocation(string path) => _inner.SupportsSparsePreallocation(path);

    public bool IsSameVolume(string pathA, string pathB) => _inner.IsSameVolume(pathA, pathB);

    public void ValidatePathWithinRoot(string path, string rootPath) =>
        _inner.ValidatePathWithinRoot(path, rootPath);

    private sealed class FaultInjectingWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _failAtOffset;
        private long _bytesWritten;

        public FaultInjectingWriteStream(Stream inner, long failAtOffset)
        {
            _inner = inner;
            _failAtOffset = failAtOffset;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfFault(count);
            _inner.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowIfFault(count);
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfFault(buffer.Length);
            return _inner.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void ThrowIfFault(int count)
        {
            if (_bytesWritten + count > _failAtOffset && _bytesWritten < _failAtOffset)
            {
                throw new IOException("Injected write fault.", unchecked((int)0x80070015));
            }

            _bytesWritten += count;
        }
    }
}
