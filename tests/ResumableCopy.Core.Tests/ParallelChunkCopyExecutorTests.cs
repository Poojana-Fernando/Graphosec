using ResumableCopy.Core.Concurrency;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Reliability;
using ResumableCopy.Core.Tests.TestSupport;
using System.Threading.Channels;

namespace ResumableCopy.Core.Tests;

public class ParallelChunkCopyExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenWorkerFailsWithDeviceDisconnect_RethrowsOriginalFailureNotChannelClosed()
    {
        using var temp = new TempDirectory();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        var stagingPath = temp.GetPath("dest", ".copycache", "staging.part");
        Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(64 * 1024));

        var context = new FaultInjectionTestContext(
            new FaultRule
            {
                Point = FaultPoint.BeforeChunkWrite,
                Kind = FaultKind.DeviceDisconnect,
                ChunkIndex = 2,
                Occurrence = 1
            });

        var sourceIdentity = context.SourceIdentityProvider.Capture(sourcePath);
        var chunkSize = 8 * 1024;
        var totalChunks = 8;
        var session = new CopySession
        {
            SessionId = "parallel-failure",
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            StagingPath = stagingPath,
            SourceIdentity = sourceIdentity,
            ChunkSize = chunkSize,
            TotalChunks = totalChunks,
            Chunks = Enumerable.Range(0, totalChunks)
                .Select(index => new ChunkRecord
                {
                    Index = index,
                    Offset = index * chunkSize,
                    Length = chunkSize
                })
                .ToList()
        };

        var repository = context.SessionRepositoryProvider.GetRepository(destinationPath);
        await repository.SaveAsync(session, CancellationToken.None);

        var options = new CopyOptions
        {
            ChunkSize = chunkSize,
            MaximumWorkers = 4,
            MaximumQueuedChunks = 8
        };

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            context.ChunkCopyExecutor.ExecuteAsync(
                session,
                options,
                repository,
                progress: null,
                CancellationToken.None));

        Assert.IsNotType<ChannelClosedException>(exception);
        var classified = TransientErrorClassifier.Classify(exception, "Copy operation failed");
        Assert.IsType<DestinationUnavailableException>(classified);
    }

    private static byte[] CreateDeterministicBytes(int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < length; index++)
        {
            bytes[index] = (byte)(index % 251);
        }

        return bytes;
    }
}
