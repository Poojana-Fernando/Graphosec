using System.Windows;
using System.Windows.Threading;
using ResumableCopy.Application.Abstractions;

namespace ResumableCopy.App.Services;

public sealed class WpfUiThread : IUiThread
{
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = GetDispatcher();
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = GetDispatcher();
        _ = dispatcher.InvokeAsync(action, DispatcherPriority.Background);
    }

    private static Dispatcher GetDispatcher() =>
        System.Windows.Application.Current?.Dispatcher
        ?? throw new InvalidOperationException("The WPF application dispatcher is not available.");
}
