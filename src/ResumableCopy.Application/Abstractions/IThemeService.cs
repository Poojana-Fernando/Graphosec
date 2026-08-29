using ResumableCopy.Application.Models;

namespace ResumableCopy.Application.Abstractions;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    void ApplyTheme(AppTheme theme);

    event EventHandler? ThemeChanged;
}
