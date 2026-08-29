using ResumableCopy.Core.Domain;

namespace ResumableCopy.Application.Services;

public sealed class ThrottledProgressReporter : IProgress<CopyProgress>
{
    private readonly IProgress<CopyProgress> _inner;
    private readonly TimeSpan _minimumInterval;
    private readonly object _sync = new();
    private DateTimeOffset _lastReportUtc = DateTimeOffset.MinValue;
    private CopyProgress? _latest;

    public ThrottledProgressReporter(IProgress<CopyProgress> inner, TimeSpan? minimumInterval = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _minimumInterval = minimumInterval ?? TimeSpan.FromMilliseconds(200);
    }

    public void Report(CopyProgress value)
    {
        lock (_sync)
        {
            _latest = value;
            var now = DateTimeOffset.UtcNow;
            if (now - _lastReportUtc < _minimumInterval
                && value.State is not CopyState.Completed
                    and not CopyState.Failed
                    and not CopyState.Pending
                    and not CopyState.Verifying)
            {
                return;
            }

            _lastReportUtc = now;
            _inner.Report(value);
        }
    }

    public void Flush()
    {
        lock (_sync)
        {
            if (_latest is null)
            {
                return;
            }

            _inner.Report(_latest);
            _lastReportUtc = DateTimeOffset.UtcNow;
        }
    }
}
