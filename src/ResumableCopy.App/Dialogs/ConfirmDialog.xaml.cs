using System.Windows;

namespace ResumableCopy.App.Dialogs;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageTextBlock.Text = message;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
