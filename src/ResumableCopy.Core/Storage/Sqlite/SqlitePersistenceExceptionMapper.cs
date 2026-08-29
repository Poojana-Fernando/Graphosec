using Microsoft.Data.Sqlite;
using ResumableCopy.Core.Errors;

namespace ResumableCopy.Core.Storage.Sqlite;

internal static class SqlitePersistenceExceptionMapper
{
    private const int SqliteIoErr = 10;
    private const int SqliteCantOpen = 14;

    public static Exception Map(string operation, SqliteException exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);

        if (IsDestinationUnavailable(exception))
        {
            return new DestinationUnavailableException(
                $"{operation}: destination storage is unavailable.",
                exception);
        }

        return new SessionPersistenceException($"Unable to {operation}.", exception);
    }

    private static bool IsDestinationUnavailable(SqliteException exception) =>
        exception.SqliteErrorCode is SqliteCantOpen or SqliteIoErr;
}
