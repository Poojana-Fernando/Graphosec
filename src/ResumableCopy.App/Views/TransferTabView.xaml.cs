using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace ResumableCopy.App.Views;

public partial class TransferTabView : UserControl
{
    public static readonly DependencyProperty TransfersSourceProperty =
        DependencyProperty.Register(
            nameof(TransfersSource),
            typeof(IEnumerable),
            typeof(TransferTabView),
            new PropertyMetadata(null));

    public IEnumerable? TransfersSource
    {
        get => (IEnumerable?)GetValue(TransfersSourceProperty);
        set => SetValue(TransfersSourceProperty, value);
    }

    public TransferTabView()
    {
        InitializeComponent();
    }
}
