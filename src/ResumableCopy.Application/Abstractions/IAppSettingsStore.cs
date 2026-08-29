using ResumableCopy.Application.Models;

namespace ResumableCopy.Application.Abstractions;

public interface IAppSettingsStore
{
    AppTheme LoadTheme();

    void SaveTheme(AppTheme theme);
}
