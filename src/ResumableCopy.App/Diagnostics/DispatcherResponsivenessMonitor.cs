using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace ResumableCopy.App.Diagnostics;

public sealed class DispatcherResponsivenessMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly ILogger _logger;
    private readonly TimeSpan _threshold;
    private readonly TimeSpan _interval;
    private long _lastTickTimestamp;
    private bool _disposed;

    public DispatcherResponsivenessMonitor(Dispatcher dispatcher, ILogger logger, TimeSpan threshold)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _threshold = threshold > TimeSpan.Zero ? threshold : TimeSpan.FromMilliseconds(500);
        _interval = TimeSpan.FromSeconds(1);
        _lastTickTimestamp = Stopwatch.GetTimestamp();

        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = _interval
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastTickTimestamp);
        _lastTickTimestamp = now;

        var stall = elapsed - _interval;
        if (stall >= _threshold)
        {
            _logger.LogWarning(
                "UI thread blocked for {DelayMs} ms (threshold {ThresholdMs} ms).",
                stall.TotalMilliseconds,
                _threshold.TotalMilliseconds);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
