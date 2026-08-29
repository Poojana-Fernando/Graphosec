using ResumableCopy.Core.Concurrency;

namespace ResumableCopy.Core.Tests;

public class ChunkFlushTrackerTests
{
    [Fact]
    public async Task TrackAndMaybeFlushAsync_WhenIntervalReached_FlushesStream()
    {
        var tracker = new ChunkFlushTracker();
        await using var stream = new FlushCountingStream();

        await tracker.TrackAndMaybeFlushAsync(stream, 32 * 1024, flushEveryChunk: false, flushIntervalBytes: 64 * 1024, CancellationToken.None);
        Assert.Equal(0, stream.FlushCount);

        await tracker.TrackAndMaybeFlushAsync(stream, 64 * 1024, flushEveryChunk: false, flushIntervalBytes: 64 * 1024, CancellationToken.None);
        Assert.Equal(1, stream.FlushCount);
    }

    [Fact]
    public async Task FlushRemainingAsync_FlushesPendingBytes()
    {
        var tracker = new ChunkFlushTracker();
        await using var stream = new FlushCountingStream();

        await tracker.TrackAndMaybeFlushAsync(stream, 1024, flushEveryChunk: false, flushIntervalBytes: 64 * 1024, CancellationToken.None);
        Assert.Equal(0, stream.FlushCount);

        await tracker.FlushRemainingAsync(stream, CancellationToken.None);
        Assert.Equal(1, stream.FlushCount);
    }

    private sealed class FlushCountingStream : Stream
    {
        private readonly MemoryStream _inner = new();

        public int FlushCount { get; private set; }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush()
        {
            FlushCount++;
            _inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            return _inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

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
