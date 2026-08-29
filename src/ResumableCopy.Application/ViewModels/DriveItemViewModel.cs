using CommunityToolkit.Mvvm.ComponentModel;
using ResumableCopy.Application.Models;

namespace ResumableCopy.Application.ViewModels;

public sealed partial class DriveItemViewModel : ObservableObject
{
    public DriveItemViewModel(DriveInfoSnapshot snapshot)
    {
        RootPath = snapshot.RootPath;
        DisplayName = snapshot.DisplayName;
        DriveType = snapshot.DriveType;
        IsRemovable = snapshot.IsRemovable;
        IsReady = snapshot.IsReady;
        StatusText = snapshot.IsReady ? "Ready" : "Not ready";
        SpaceText = FormatSpace(snapshot.FreeBytes, snapshot.TotalBytes);
    }

    public string RootPath { get; }

    public string DisplayName { get; }

    public string DriveType { get; }

    public bool IsRemovable { get; }

    public bool IsReady { get; }

    public string StatusText { get; }

    public string SpaceText { get; }

    private static string FormatSpace(long? freeBytes, long? totalBytes)
    {
        if (freeBytes is null || totalBytes is null)
        {
            return "—";
        }

        return $"{FormatBytes(freeBytes.Value)} free / {FormatBytes(totalBytes.Value)}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
