using System.Diagnostics;
using ResumableCopy.Core.Core;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Performance;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

[Trait("Category", "Benchmark")]
public class PerformanceBenchmarkTests
{
    [Fact]
    public async Task Benchmark_MediumFile_RecordsBaselineAndOptimizedThroughput()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        const int fileSize = 8 * 1024 * 1024;
        var sourcePath = temp.GetPath("medium.bin");
        var baselineDestination = temp.GetPath("dest", "baseline.bin");
        var optimizedDestination = temp.GetPath("dest", "optimized.bin");
        await File.WriteAllBytesAsync(sourcePath, CreateDeterministicBytes(fileSize));

        var baselineOptions = new CopyOptions
        {
            ChunkSize = 64 * 1024,
            MaximumWorkers = 1,
            MaximumQueuedChunks = 2,
            IoBufferSize = 32 * 1024,
            UseAdaptivePerformance = false,
            VerifyWholeFileAfterCopy = false
        };

        var optimizedOptions = new CopyOptions
        {
            UseAdaptivePerformance = true,
            VerifyWholeFileAfterCopy = false
        };

        var baseline = await MeasureCopyAsync(context.Engine, sourcePath, baselineDestination, baselineOptions, "Medium file");
        var optimized = await MeasureCopyAsync(context.Engine, sourcePath, optimizedDestination, optimizedOptions, "Medium file");

        Assert.Equal(CopyState.Completed, baseline.State);
        Assert.Equal(CopyState.Completed, optimized.State);
        Assert.Equal(fileSize, new FileInfo(baselineDestination).Length);
        Assert.Equal(fileSize, new FileInfo(optimizedDestination).Length);

        var report = PerformanceMeasurementRecorder.FormatComparison(baseline.Measurement, optimized.Measurement);
        Assert.Contains("Before:", report);
        Assert.Contains("After:", report);
    }

    [Fact]
    public async Task Benchmark_LargeFile_ParallelWorkersCompleteCorrectly()
    {
        using var temp = new TempDirectory();
        var context = new SqliteCopyEngineTestContext();
        const int fileSize = 16 * 1024 * 1024;
        var sourcePath = temp.GetPath("large.bin");
        var destinationPath = temp.GetPath("dest", "large-out.bin");
        var sourceBytes = CreateDeterministicBytes(fileSize);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var options = new CopyOptions
        {
            ChunkSize = 1024 * 1024,
            MaximumWorkers = 4,
            MaximumQueuedChunks = 8,
            IoBufferSize = 128 * 1024,
            UseAdaptivePerformance = false,
            VerifyWholeFileAfterCopy = true
        };

        var result = await MeasureCopyAsync(context.Engine, sourcePath, destinationPath, options, "Large file parallel");
        Assert.Equal(CopyState.Completed, result.State);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
        Assert.True(result.Measurement.ThroughputMegabytesPerSecond > 0);
    }

    private static async Task<(CopyState State, PerformanceMeasurement Measurement)> MeasureCopyAsync(
        CopyEngine engine,
        string sourcePath,
        string destinationPath,
        CopyOptions options,
        string workload)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var process = Process.GetCurrentProcess();
        var workingSetBefore = process.WorkingSet64;
        var stopwatch = Stopwatch.StartNew();

        var copyResult = await engine.CopyAsync(
            new CopyJob(sourcePath, destinationPath, options),
            progress: null,
            CancellationToken.None);

        stopwatch.Stop();
        var workingSetAfter = process.WorkingSet64;

        var configuration =
            $"chunk={options.ChunkSize}, workers={options.MaximumWorkers}, queue={options.MaximumQueuedChunks}, buffer={options.IoBufferSize}, adaptive={options.UseAdaptivePerformance}";

        var measurement = PerformanceMeasurementRecorder.Measure(
            workload,
            configuration,
            new FileInfo(destinationPath).Length,
            stopwatch.Elapsed,
            Math.Max(workingSetBefore, workingSetAfter));

        return (copyResult.FinalState, measurement);
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
