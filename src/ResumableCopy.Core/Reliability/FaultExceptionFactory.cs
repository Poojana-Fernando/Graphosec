using ResumableCopy.Core.Errors;

namespace ResumableCopy.Core.Reliability;

internal static class FaultExceptionFactory
{
    private const int HResultDeviceNotReady = unchecked((int)0x80070015);
    private const int HResultDiskFull = unchecked((int)0x80070070);

    public static Exception Create(FaultKind kind, FaultPoint point, FaultContext context)
    {
        var chunkLabel = context.ChunkIndex is int chunkIndex ? $"chunk {chunkIndex}" : "transfer";
        var message = $"Injected {kind} fault at {point} for {chunkLabel}.";

        return kind switch
        {
            FaultKind.ReadFailure => new IOException(message, HResultDeviceNotReady),
            FaultKind.WriteFailure => new IOException(message, HResultDeviceNotReady),
            FaultKind.AccessDenied => new UnauthorizedAccessException(message),
            FaultKind.DiskFull => new IOException(message, HResultDiskFull),
            FaultKind.DeviceDisconnect => new IOException(message, HResultDeviceNotReady),
            FaultKind.HashMismatch => new IntegrityException(message),
            FaultKind.DatabaseFailure => new SessionPersistenceException(message),
            FaultKind.SlowIo => new InvalidOperationException(message),
            FaultKind.CorruptBytes => new InvalidOperationException(message),
            _ => new InvalidOperationException(message)
        };
    }
}
