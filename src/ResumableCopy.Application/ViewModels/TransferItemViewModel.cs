using CommunityToolkit.Mvvm.ComponentModel;
using ResumableCopy.Core.Domain;

namespace ResumableCopy.Application.ViewModels;

public partial class TransferItemViewModel : ObservableObject
{
    [ObservableProperty]
    private CopyState _state;

    [ObservableProperty]
    private string _sessionId = string.Empty;

    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _bytesCopiedText = string.Empty;

    [ObservableProperty]
    private string _speedText = string.Empty;

    [ObservableProperty]
    private string _etaText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _userMessage;

    [ObservableProperty]
    private bool _canPause;

    [ObservableProperty]
    private bool _canResume;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private bool _canRetry;

    [ObservableProperty]
    private bool _canRemove;
}
