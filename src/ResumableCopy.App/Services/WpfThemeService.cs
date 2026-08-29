using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Models;
using WpfApplication = System.Windows.Application;

namespace ResumableCopy.App.Services;

public sealed class WpfThemeService : IThemeService
{
    private readonly IAppSettingsStore _settingsStore;

    public WpfThemeService(IAppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        CurrentTheme = _settingsStore.LoadTheme();
    }

    public AppTheme CurrentTheme { get; private set; }

    public event EventHandler? ThemeChanged;

    public void ApplyTheme(AppTheme theme)
    {
        if (theme == CurrentTheme && ThemeDictionaryLoaded())
        {
            return;
        }

        var app = WpfApplication.Current;
        var dictionaries = app.Resources.MergedDictionaries;
        var existingTheme = dictionaries
            .FirstOrDefault(dictionary => dictionary.Source?.OriginalString.Contains("Theme.xaml", StringComparison.OrdinalIgnoreCase) == true);
        if (existingTheme is not null)
        {
            dictionaries.Remove(existingTheme);
        }

        var themeUri = theme == AppTheme.Dark
            ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("Themes/LightTheme.xaml", UriKind.Relative);
        dictionaries.Add(new System.Windows.ResourceDictionary { Source = themeUri });

        CurrentTheme = theme;
        _settingsStore.SaveTheme(theme);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool ThemeDictionaryLoaded()
    {
        return WpfApplication.Current.Resources.MergedDictionaries
            .Any(dictionary => dictionary.Source?.OriginalString.Contains("Theme.xaml", StringComparison.OrdinalIgnoreCase) == true);
    }
}
