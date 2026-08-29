using Microsoft.Data.Sqlite;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Storage.Sqlite;

namespace ResumableCopy.Core.Tests;

public class SqlitePersistenceExceptionMapperTests
{
    [Theory]
    [InlineData(14)]
    [InlineData(10)]
    public void Map_WhenDestinationStorageUnavailable_ReturnsDestinationUnavailable(int errorCode)
    {
        var sqliteException = new SqliteException("SQLite failure.", errorCode);

        var mapped = SqlitePersistenceExceptionMapper.Map("open the transfer session database", sqliteException);

        var destinationUnavailable = Assert.IsType<DestinationUnavailableException>(mapped);
        Assert.Equal(CopyFailureKind.Recoverable, destinationUnavailable.FailureKind);
        Assert.Contains("destination storage is unavailable", destinationUnavailable.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_WhenDatabaseIsCorrupt_ReturnsSessionPersistenceException()
    {
        var sqliteException = new SqliteException("SQLite failure.", 11);

        var mapped = SqlitePersistenceExceptionMapper.Map("open the transfer session database", sqliteException);

        var persistenceException = Assert.IsType<SessionPersistenceException>(mapped);
        Assert.Equal(CopyFailureKind.Permanent, persistenceException.FailureKind);
    }

    [Fact]
    public void Classify_SessionPersistenceExceptionWithCantOpen_MapsToDestinationUnavailable()
    {
        var sqliteException = new SqliteException("unable to open database file", 14);
        var persistenceException = new SessionPersistenceException(
            "Unable to open the transfer session database.",
            sqliteException);

        var classified = TransientErrorClassifier.Classify(persistenceException, "Transfer failed");

        Assert.IsType<DestinationUnavailableException>(classified);
    }
}
