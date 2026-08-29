namespace ResumableCopy.Application.Abstractions;

public interface IFilePickerService
{
    string? PickSourceFile();

    string? PickDestinationFile(string? sourceFilePath);
}
