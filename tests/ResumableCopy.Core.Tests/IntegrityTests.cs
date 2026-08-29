using ResumableCopy.Core.Integrity;

namespace ResumableCopy.Core.Tests;

public class IntegrityTests
{
    [Fact]
    public void Sha256HashService_KnownVector_MatchesExpected()
    {
        var service = new Sha256HashService();
        var hash = service.ComputeHash("abc"u8.ToArray());

        Assert.Equal("sha256", service.AlgorithmId);
        Assert.Equal(32, service.HashSizeInBytes);
        Assert.Equal(
            Convert.FromHexString("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"),
            hash);
    }

    [Fact]
    public void ChunkVerifier_AcceptsMatchingHash()
    {
        var hashService = new Sha256HashService();
        var verifier = new ChunkVerifier(hashService);
        var data = "chunk-data"u8.ToArray();
        var hash = hashService.ComputeHash(data);

        Assert.True(verifier.Verify(data, hash));
    }

    [Fact]
    public void ChunkVerifier_RejectsMismatchedHash()
    {
        var hashService = new Sha256HashService();
        var verifier = new ChunkVerifier(hashService);
        var data = "chunk-data"u8.ToArray();
        var wrongHash = hashService.ComputeHash("other"u8.ToArray());

        Assert.False(verifier.Verify(data, wrongHash));
    }

    [Fact]
    public async Task FileVerifier_ReturnsFalseForMismatchedFile()
    {
        using var temp = new TestSupport.TempDirectory();
        var hashService = new Sha256HashService();
        var verifier = new FileVerifier(hashService);
        var filePath = temp.GetPath("file.bin");
        await File.WriteAllBytesAsync(filePath, [1, 2, 3, 4]);

        var wrongHash = hashService.ComputeHash("not-the-file"u8.ToArray());
        var verified = await verifier.VerifyAsync(filePath, wrongHash, CancellationToken.None);

        Assert.False(verified);
    }
}
