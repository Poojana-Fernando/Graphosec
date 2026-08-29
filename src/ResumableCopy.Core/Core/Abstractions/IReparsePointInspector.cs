namespace ResumableCopy.Core.Abstractions;

public interface IReparsePointInspector
{
    void EnsureRegularFile(string path);

    bool IsReparsePoint(string path);
}
