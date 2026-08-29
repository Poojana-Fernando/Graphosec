using System.Windows;
using ResumableCopy.App.Dialogs;
using ResumableCopy.Application.Abstractions;

namespace ResumableCopy.App.Services;

public sealed class WpfUserPromptService : IUserPromptService
{
    public bool Confirm(string title, string message)
    {
        var dialog = new ConfirmDialog(title, message)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        return dialog.ShowDialog() == true;
    }
}
