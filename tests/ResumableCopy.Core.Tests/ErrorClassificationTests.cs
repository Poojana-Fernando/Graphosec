using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Storage;

namespace ResumableCopy.Core.Tests;

public class ErrorClassificationTests
{
    [Fact]
    public void Classify_SharingViolation_IsRecoverableDestinationUnavailable()
    {
        var exception = new IOException("sharing violation", unchecked((int)0x80070020));
        var classified = TransientErrorClassifier.Classify(exception, "write chunk");

        Assert.IsType<DestinationUnavailableException>(classified);
        Assert.Equal(CopyFailureKind.Recoverable, classified.FailureKind);
    }

    [Fact]
    public void Classify_DiskFull_IsPermanentInsufficientStorage()
    {
        var exception = new IOException("disk full", unchecked((int)0x80070027));
        var classified = TransientErrorClassifier.Classify(exception, "write chunk");

        Assert.IsType<InsufficientStorageException>(classified);
        Assert.Equal(CopyFailureKind.Permanent, classified.FailureKind);
    }

    [Fact]
    public void Classify_UnauthorizedAccess_IsPermissionDenied()
    {
        var classified = TransientErrorClassifier.Classify(
            new UnauthorizedAccessException("denied"),
            "open destination");

        Assert.IsType<PermissionDeniedException>(classified);
        Assert.Equal(CopyFailureKind.Permanent, classified.FailureKind);
    }

    [Fact]
    public void IsRecoverableIo_DeviceNotReady_ReturnsTrue()
    {
        var exception = new IOException("not ready", unchecked((int)0x80070015));
        Assert.True(TransientErrorClassifier.IsRecoverableIo(exception));
    }
}
