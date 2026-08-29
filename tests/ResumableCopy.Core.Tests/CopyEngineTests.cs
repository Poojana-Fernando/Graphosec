using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Storage;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class CopyEngineTests
{
    [Fact]
    public async Task CopyAsync_EmptyFile_CreatesEmptyDestination()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourcePath = temp.GetPath("empty.bin");
        var destinationPath = temp.GetPath("dest", "empty.bin");
        await File.WriteAllBytesAsync(sourcePath, []);

        var result = await context.Engine.CopyAsync(
            new CopyJob(sourcePath, destinationPath),
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.True(File.Exists(destinationPath));
        Assert.Equal(0, new FileInfo(destinationPath).Length);
    }

    [Fact]
    public async Task CopyAsync_SmallFile_CopiesExactBytes()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourceBytes = "hello-resumable-copy"u8.ToArray();
        var sourcePath = temp.GetPath("small.bin");
        var destinationPath = temp.GetPath("dest", "small.bin");
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var result = await context.Engine.CopyAsync(
            new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 8 }),
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task CopyAsync_UsesProvidedSessionId()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourceBytes = "session-id-test"u8.ToArray();
        var sourcePath = temp.GetPath("small.bin");
        var destinationPath = temp.GetPath("dest", "small.bin");
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        var expectedSessionId = Guid.NewGuid().ToString("N");

        var result = await context.Engine.CopyAsync(
            new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 8 }, expectedSessionId),
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(expectedSessionId, result.SessionId);
    }

    [Fact]
    public async Task CopyAsync_MultiChunkFile_CopiesExactBytes()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourceBytes = CreateDeterministicBytes(256 * 1024 + 137);
        var sourcePath = temp.GetPath("large.bin");
        var destinationPath = temp.GetPath("dest", "large.bin");
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var result = await context.Engine.CopyAsync(
            new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 64 * 1024 }),
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task CopyAsync_UnicodeFilename_CopiesSuccessfully()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourceBytes = CreateDeterministicBytes(4096);
        var sourcePath = temp.GetPath("源ファイル_📁.bin");
        var destinationPath = temp.GetPath("dest", "目标文件_📁.bin");
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var result = await context.Engine.CopyAsync(
            new CopyJob(sourcePath, destinationPath, new CopyOptions { ChunkSize = 1024 }),
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task CopyAsync_ExistingDestinationWithoutOverwrite_Fails()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "target.bin");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, [9, 9, 9]);

        await Assert.ThrowsAsync<CopyException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath),
                progress: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task CopyAsync_ExistingDestinationWithOverwrite_ReplacesFile()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourcePath = temp.GetPath("source.bin");
        var destinationPath = temp.GetPath("dest", "target.bin");
        var sourceBytes = CreateDeterministicBytes(8192);
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, [9, 9, 9]);

        var result = await context.Engine.CopyAsync(
            new CopyJob(sourcePath, destinationPath, new CopyOptions
            {
                ChunkSize = 2048,
                OverwriteExisting = true
            }),
            progress: null,
            CancellationToken.None);

        Assert.Equal(CopyState.Completed, result.FinalState);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task CopyAsync_MissingSource_ThrowsSourceUnavailable()
    {
        using var temp = new TempDirectory();
        var context = new CopyEngineTestContext();
        var sourcePath = temp.GetPath("missing.bin");
        var destinationPath = temp.GetPath("dest", "out.bin");

        await Assert.ThrowsAsync<SourceUnavailableException>(() =>
            context.Engine.CopyAsync(
                new CopyJob(sourcePath, destinationPath),
                progress: null,
                CancellationToken.None));
    }

    private static byte[] CreateDeterministicBytes(int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < length; index++)
        {
            bytes[index] = (byte)(index % 251);
        }

        return bytes;
    }
}
