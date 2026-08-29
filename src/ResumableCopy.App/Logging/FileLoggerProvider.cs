using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ResumableCopy.App.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly LogLevel _minimumLevel;
    private readonly object _sync = new();

    public FileLoggerProvider(string logDirectory, LogLevel minimumLevel)
    {
        _logDirectory = ExpandEnvironmentVariables(logDirectory);
        _minimumLevel = minimumLevel;
        Directory.CreateDirectory(_logDirectory);
    }

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, Path.Combine(_logDirectory, $"resumablecopy-{DateTime.UtcNow:yyyyMMdd}.log"), _minimumLevel, _sync);

    public void Dispose()
    {
    }

    private static string ExpandEnvironmentVariables(string path) =>
        Environment.ExpandEnvironmentVariables(path);

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _logFilePath;
        private readonly LogLevel _minimumLevel;
        private readonly object _sync;

        public FileLogger(string categoryName, string logFilePath, LogLevel minimumLevel, object sync)
        {
            _categoryName = categoryName;
            _logFilePath = logFilePath;
            _minimumLevel = minimumLevel;
            _sync = sync;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(" [");
            builder.Append(logLevel);
            builder.Append("] ");
            builder.Append(_categoryName);
            builder.Append(": ");
            builder.Append(formatter(state, exception));

            if (exception is not null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }

            lock (_sync)
            {
                File.AppendAllText(_logFilePath, builder.ToString() + Environment.NewLine, Encoding.UTF8);
            }
        }
    }
}
