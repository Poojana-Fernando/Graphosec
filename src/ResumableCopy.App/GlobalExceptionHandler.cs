using System.Text;
using System.Windows;
using Microsoft.Extensions.Logging;
using ResumableCopy.Application.Configuration;
using ResumableCopy.Application.Services;

namespace ResumableCopy.App;

public static class GlobalExceptionHandler
{
    public static void Register(System.Windows.Application app, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(logger);

        app.DispatcherUnhandledException += (_, args) =>
        {
            LogException(logger, "Unhandled dispatcher exception.", args.Exception);
            args.Handled = true;
            ShowFatalMessage(args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogException(logger, "Unhandled AppDomain exception.", exception);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException(logger, "Unobserved task exception.", args.Exception, LogLevel.Error);
            args.SetObserved();
        };
    }

    private static void LogException(ILogger logger, string message, Exception exception, LogLevel level = LogLevel.Critical)
    {
        logger.Log(
            level,
            exception,
            "{Message} Type={ExceptionType} Version={Version} InnerType={InnerType} InnerMessage={InnerMessage} StackTrace={StackTrace}",
            message,
            exception.GetType().FullName,
            ApplicationInfo.Version,
            exception.InnerException?.GetType().FullName ?? "(none)",
            Sanitize(exception.InnerException?.Message),
            exception.ToString());
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsControl(character) && character is not '\r' and not '\n' and not '\t' ? '?' : character);
        }

        return builder.ToString();
    }

    private static void ShowFatalMessage(Exception exception)
    {
        var message = UserMessageFormatter.GetUserMessage(exception);
        MessageBox.Show(
            message,
            $"{ApplicationInfo.ProductName} encountered an unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
