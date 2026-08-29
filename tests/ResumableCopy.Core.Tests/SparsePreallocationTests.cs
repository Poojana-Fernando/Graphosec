using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Concurrency;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Integrity;
using ResumableCopy.Core.IO;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class SparsePreallocationTests
{
    [Fact]
    public async Task ExecuteAsync_OnNonSparseVolume_SkipsUpfrontSetLength()
    {
        using var temp = new TempDirectory();
        var sourcePath = temp.GetPath("source.bin");
        var stagingPath = temp.GetPath("staging.part");
        await File.WriteAllBytesAsync(sourcePath, new byte[4096]);

        var context = new CopyEngineTestContext();
        var fileSystem = new SelectiveSparseFileSystem(new FileSystemService(), supportsSparse: false);
        var executor = new ParallelChunkCopyExecutor(
            fileSystem,
            new Sha256HashService(),
            new ChunkVerifier(new Sha256HashService()),
            context.EnvironmentMonitor);

        var sourceIdentity = context.SourceIdentityProvider.Capture(sourcePath);
        var session = new CopySession
        {
            SessionId = "s1",
            SourcePath = sourcePath,
            DestinationPath = temp.GetPath("dest.bin"),
            StagingPath = stagingPath,
            SourceIdentity = sourceIdentity,
            ChunkSize = 4096,
            TotalChunks = 1,
            Chunks =
            [
                new ChunkRecord { Index = 0, Offset = 0, Length = 4096 }
            ]
        };

        var repository = context.SessionRepositoryProvider.GetRepository(temp.GetPath("dest.bin"));
        await repository.SaveAsync(session, CancellationToken.None);

        await executor.ExecuteAsync(
            session,
            new CopyOptions { MaximumWorkers = 1 },
            repository,
            progress: null,
            CancellationToken.None);

        Assert.False(fileSystem.SetLengthCalled);
        Assert.Equal(4096, new FileInfo(stagingPath).Length);
    }

    [Fact]
    public void SupportsSparsePreallocation_OnNtfsRoot_ReturnsTrue()
    {
        var fileSystem = new FileSystemService();
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)!;

        Assert.True(fileSystem.SupportsSparsePreallocation(Path.Combine(systemDrive, "file.bin")));
    }

    private sealed class SelectiveSparseFileSystem : IFileSystemService
    {
        private readonly IFileSystemService _inner;
        private readonly bool _supportsSparse;

        public SelectiveSparseFileSystem(IFileSystemService inner, bool supportsSparse)
        {
            _inner = inner;
            _supportsSparse = supportsSparse;
        }

        public bool SetLengthCalled { get; private set; }

        public bool FileExists(string path) => _inner.FileExists(path);

        public FileMetadata GetMetadata(string path) => _inner.GetMetadata(path);

        public Stream OpenRead(string path, int ioBufferSize = CopyOptions.DefaultIoBufferSize) =>
            _inner.OpenRead(path, ioBufferSize);

        public Stream OpenWrite(string path, bool createNew, int ioBufferSize = CopyOptions.DefaultIoBufferSize)
        {
            var stream = _inner.OpenWrite(path, createNew, ioBufferSize);
            return new SetLengthTrackingStream(stream, () => SetLengthCalled = true);
        }

        public Stream OpenReadWrite(string path, bool createNew, FileShare share, int ioBufferSize = CopyOptions.DefaultIoBufferSize)
        {
            var stream = _inner.OpenReadWrite(path, createNew, share, ioBufferSize);
            return new SetLengthTrackingStream(stream, () => SetLengthCalled = true);
        }

        public void EnsureDirectory(string path) => _inner.EnsureDirectory(path);

        public void ReplaceOrMove(string sourcePath, string destinationPath, bool overwrite) =>
            _inner.ReplaceOrMove(sourcePath, destinationPath, overwrite);

        public void Delete(string path) => _inner.Delete(path);

        public long GetAvailableFreeSpace(string path) => _inner.GetAvailableFreeSpace(path);

        public bool SupportsSparsePreallocation(string path) => _supportsSparse;

        public bool IsSameVolume(string pathA, string pathB) => _inner.IsSameVolume(pathA, pathB);

        public void ValidatePathWithinRoot(string path, string rootPath) =>
            _inner.ValidatePathWithinRoot(path, rootPath);

        private sealed class SetLengthTrackingStream : Stream
        {
            private readonly Stream _inner;
            private readonly Action _onSetLength;

            public SetLengthTrackingStream(Stream inner, Action onSetLength)
            {
                _inner = inner;
                _onSetLength = onSetLength;
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

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

            public override void SetLength(long value)
            {
                _onSetLength();
                _inner.SetLength(value);
            }

            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
