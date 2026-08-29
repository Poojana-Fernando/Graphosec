namespace ResumableCopy.Core.Concurrency;

internal sealed class ChunkFlushTracker
{
    private readonly object _sync = new();
    private long _bytesSinceLastFlush;

    public async Task TrackAndMaybeFlushAsync(
        Stream destinationStream,
        int bytesWritten,
        bool flushEveryChunk,
        long flushIntervalBytes,
        CancellationToken cancellationToken)
    {
        if (flushEveryChunk)
        {
            await FlushToDiskAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (flushIntervalBytes <= 0)
        {
            return;
        }

        var shouldFlush = false;
        lock (_sync)
        {
            _bytesSinceLastFlush += bytesWritten;
            if (_bytesSinceLastFlush >= flushIntervalBytes)
            {
                _bytesSinceLastFlush = 0;
                shouldFlush = true;
            }
        }

        if (shouldFlush)
        {
            await FlushToDiskAsync(destinationStream, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task FlushRemainingAsync(Stream destinationStream, CancellationToken cancellationToken)
    {
        bool hasPending;
        lock (_sync)
        {
            hasPending = _bytesSinceLastFlush > 0;
            _bytesSinceLastFlush = 0;
        }

        if (hasPending)
        {
            await FlushToDiskAsync(destinationStream, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task FlushToDiskAsync(Stream destinationStream, CancellationToken cancellationToken)
    {
        await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (destinationStream is FileStream fileStream)
        {
            fileStream.Flush(flushToDisk: true);
        }
    }
}
