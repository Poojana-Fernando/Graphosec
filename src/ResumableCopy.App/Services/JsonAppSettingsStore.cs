using System.IO;
using System.Text.Json;
using ResumableCopy.Application.Abstractions;
using ResumableCopy.Application.Models;

namespace ResumableCopy.App.Services;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public JsonAppSettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Graphosec");
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "ui-settings.json");
    }

    public AppTheme LoadTheme()
    {
        if (!File.Exists(_settingsPath))
        {
            return AppTheme.Light;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<UiSettingsDocument>(json, SerializerOptions);
            return settings?.Theme ?? AppTheme.Light;
        }
        catch
        {
            return AppTheme.Light;
        }
    }

    public void SaveTheme(AppTheme theme)
    {
        var settings = new UiSettingsDocument { Theme = theme };
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private sealed class UiSettingsDocument
    {
        public AppTheme Theme { get; set; } = AppTheme.Light;
    }
}
