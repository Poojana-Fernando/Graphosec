using System.IO;
using Microsoft.Win32;
using ResumableCopy.Application.Abstractions;

namespace ResumableCopy.App.Services;

public sealed class WpfFilePickerService : IFilePickerService
{
    public string? PickSourceFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select source file",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickDestinationFile(string? sourceFilePath)
    {
        var folderDialog = new OpenFolderDialog
        {
            Title = "Select destination folder",
            Multiselect = false
        };

        if (folderDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(folderDialog.FolderName))
        {
            return null;
        }

        var sourceFileName = string.IsNullOrWhiteSpace(sourceFilePath) ? null : Path.GetFileName(sourceFilePath);
        if (string.IsNullOrWhiteSpace(sourceFileName))
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Name the destination file",
                InitialDirectory = folderDialog.FolderName,
                FileName = "copy.bin",
                OverwritePrompt = true
            };

            return saveDialog.ShowDialog() == true ? saveDialog.FileName : null;
        }

        return Path.Combine(folderDialog.FolderName, sourceFileName);
    }
}
