namespace ResumableCopy.Core.Reliability;

public enum FaultKind
{
    None = 0,
    ReadFailure = 1,
    WriteFailure = 2,
    AccessDenied = 3,
    DiskFull = 4,
    DeviceDisconnect = 5,
    CorruptBytes = 6,
    HashMismatch = 7,
    DatabaseFailure = 8,
    SlowIo = 9
}
