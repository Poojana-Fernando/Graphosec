using Microsoft.Data.Sqlite;
using ResumableCopy.Core.Reliability;

namespace ResumableCopy.Core.Storage.Sqlite;

public sealed class SqliteSessionRepositoryOptions
{
    public Action<SqliteTransaction>? BeforeCommit { get; set; }

    public IFaultInjector? FaultInjector { get; set; }
}