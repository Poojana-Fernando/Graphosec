namespace ResumableCopy.Core.Performance;

public sealed record PerformanceMeasurement(
    string Workload,
    string Configuration,
    long BytesProcessed,
    double ElapsedMilliseconds,
    double ThroughputMegabytesPerSecond,
    long WorkingSetBytes)
{
    public override string ToString() =>
        $"{Workload} | {Configuration} | {ThroughputMegabytesPerSecond:F2} MB/s | {ElapsedMilliseconds:F0} ms | WS {WorkingSetBytes / (1024 * 1024):F1} MB";
}

public static class PerformanceMeasurementRecorder
{
    public static PerformanceMeasurement Measure(
        string workload,
        string configuration,
        long bytesProcessed,
        TimeSpan elapsed,
        long workingSetBytes) =>
        new(
            workload,
            configuration,
            bytesProcessed,
            elapsed.TotalMilliseconds,
            bytesProcessed / elapsed.TotalSeconds / (1024d * 1024d),
            workingSetBytes);

    public static string FormatComparison(PerformanceMeasurement before, PerformanceMeasurement after)
    {
        var throughputDelta = after.ThroughputMegabytesPerSecond - before.ThroughputMegabytesPerSecond;
        var throughputPercent = before.ThroughputMegabytesPerSecond <= 0
            ? 0
            : throughputDelta / before.ThroughputMegabytesPerSecond * 100d;

        return string.Join(
            Environment.NewLine,
            $"Before: {before}",
            $"After:  {after}",
            $"Throughput delta: {throughputDelta:+0.00;-0.00} MB/s ({throughputPercent:+0.0;-0.0}%)");
    }
}
