using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Integrity;
using ResumableCopy.Core.Reliability;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class IntegrityFaultTests
{
    [Fact]
    public async Task FileVerifier_DetectsModifiedStagingBytes()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var stagingPath = temp.GetPath("staging.bin");
        var sourceBytes = CreateDeterministicBytes(16 * 1024);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var tamperedBytes = (byte[])sourceBytes.Clone();
        tamperedBytes[0] ^= 0xFF;
        await File.WriteAllBytesAsync(stagingPath, tamperedBytes);

        var sourceHash = await context.HashService.ComputeFileHashAsync(sourcePath, CancellationToken.None);
        var verifier = new FileVerifier(context.HashService);

        Assert.False(await verifier.VerifyAsync(stagingPath, sourceHash, CancellationToken.None));
    }

    [Fact]
    public async Task CopyAsync_WhenHashVerificationFails_ChunkNeverMarkedComplete()
    {
        using var temp = new TempDirectory();
        var context = new FaultInjectionTestContext(new FaultRule
        {
            Point = FaultPoint.BeforeChunkVerify,
            Kind = FaultKind.HashMismatch,
            ChunkIndex = 1
        });

        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "output.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(32 * 1024));

        string? sessionId = null;
        var progress = new Progress<CopyProgress>(report => sessionId ??= report.SessionId);

        await Assert.ThrowsAsync<IntegrityException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath, new CopyOptions
                {
                    ChunkSize = 8 * 1024,
                    MaximumChunkAttempts = 1
                }),
                progress,
                CancellationToken.None));

        Assert.NotNull(sessionId);
        Assert.False(File.Exists(destinationPath));
    }

    [Fact]
    public async Task DeterministicFaultInjector_TriggersExactlyOnConfiguredOccurrence()
    {
        var rule = new FaultRule
        {
            Point = FaultPoint.BeforeChunkRead,
            Kind = FaultKind.ReadFailure,
            ChunkIndex = 3,
            Occurrence = 2
        };

        var injector = new DeterministicFaultInjector(rule);
        var context = new FaultContext { ChunkIndex = 3, AttemptNumber = 0 };

        injector.Apply(FaultPoint.BeforeChunkRead, context);
        Assert.Equal(1, injector.GetTriggerCount(rule));

        var exception = Assert.Throws<IOException>(() =>
            injector.Apply(FaultPoint.BeforeChunkRead, context));

        Assert.Contains("Injected ReadFailure", exception.Message);
        Assert.Equal(2, injector.GetTriggerCount(rule));
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
