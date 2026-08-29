using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ResumableCopy.Application.Diagnostics;

public static class OperationTimer
{
    public static IDisposable Measure(ILogger logger, string operation)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        return new Scope(logger, operation);
    }

    private sealed class Scope : IDisposable
    {
        private readonly ILogger _logger;
        private readonly string _operation;
        private readonly int _threadId;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        public Scope(ILogger logger, string operation)
        {
            _logger = logger;
            _operation = operation;
            _threadId = Environment.CurrentManagedThreadId;
            _stopwatch = Stopwatch.StartNew();
            _logger.LogDebug("Operation={Operation} started Thread={Thread}", _operation, _threadId);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopwatch.Stop();
            _logger.LogInformation(
                "Operation={Operation} DurationMs={DurationMs} Thread={Thread}",
                _operation,
                _stopwatch.Elapsed.TotalMilliseconds,
                Environment.CurrentManagedThreadId);
        }
    }
}
