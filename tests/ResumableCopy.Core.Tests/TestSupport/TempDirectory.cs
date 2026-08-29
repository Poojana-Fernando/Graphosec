using Microsoft.Data.Sqlite;

namespace ResumableCopy.Core.Tests.TestSupport;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ResumableCopyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string GetPath(params string[] relativeParts) => System.IO.Path.Combine([Path, .. relativeParts]);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
