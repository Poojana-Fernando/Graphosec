namespace ResumableCopy.Core.Reliability;

public enum FaultPoint
{
    BeforeChunkRead = 0,
    AfterChunkRead = 1,
    BeforeChunkHash = 2,
    AfterChunkHash = 3,
    BeforeChunkWrite = 4,
    AfterChunkWrite = 5,
    BeforeChunkVerify = 6,
    AfterChunkVerify = 7,
    BeforeDatabaseCommit = 8,
    AfterDatabaseCommit = 9,
    BeforeFinalization = 10,
    DuringFinalization = 11,
    AfterFinalization = 12
}
