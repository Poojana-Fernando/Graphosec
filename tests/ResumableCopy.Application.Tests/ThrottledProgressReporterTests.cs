using ResumableCopy.Application.Services;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Application.Tests;

public class ThrottledProgressReporterTests
{
    [Fact]
    public void Report_FlushesLatestProgress()
    {
        var collector = new Collector();
        var reporter = new ThrottledProgressReporter(collector, TimeSpan.FromMilliseconds(500));

        reporter.Report(new CopyProgress("s1", CopyState.Running, 100, 1000, 1, 10, 0));
        reporter.Report(new CopyProgress("s1", CopyState.Running, 200, 1000, 2, 10, 1));
        reporter.Flush();

        Assert.NotEmpty(collector.Reports);
        Assert.Equal(200, collector.Reports[^1].BytesCopied);
    }

    [Fact]
    public void Report_DropsIntermediateRunningUpdatesWithinInterval()
    {
        var collector = new Collector();
        var reporter = new ThrottledProgressReporter(collector, TimeSpan.FromSeconds(5));

        reporter.Report(new CopyProgress("s1", CopyState.Running, 100, 1000, 1, 10, 0));
        reporter.Report(new CopyProgress("s1", CopyState.Running, 200, 1000, 2, 10, 1));
        reporter.Report(new CopyProgress("s1", CopyState.Running, 300, 1000, 3, 10, 2));

        Assert.Single(collector.Reports);
        Assert.Equal(100, collector.Reports[0].BytesCopied);
    }

    [Fact]
    public void Report_AlwaysPublishesCompletedState()
    {
        var collector = new Collector();
        var reporter = new ThrottledProgressReporter(collector, TimeSpan.FromSeconds(5));

        reporter.Report(new CopyProgress("s1", CopyState.Running, 100, 1000, 1, 10, 0));
        reporter.Report(new CopyProgress("s1", CopyState.Completed, 1000, 1000, 10, 10, 9));

        Assert.Equal(2, collector.Reports.Count);
        Assert.Equal(CopyState.Completed, collector.Reports[^1].State);
    }

    [Fact]
    public void Report_AlwaysPublishesPendingState()
    {
        var collector = new Collector();
        var reporter = new ThrottledProgressReporter(collector, TimeSpan.FromSeconds(5));

        reporter.Report(new CopyProgress("s1", CopyState.Running, 100, 1000, 1, 10, 0));
        reporter.Report(new CopyProgress("s1", CopyState.Pending, 100, 1000, 1, 10, 0));

        Assert.Equal(2, collector.Reports.Count);
        Assert.Equal(CopyState.Pending, collector.Reports[^1].State);
    }

    private sealed class Collector : IProgress<CopyProgress>
    {
        public List<CopyProgress> Reports { get; } = [];

        public void Report(CopyProgress value) => Reports.Add(value);
    }
}
