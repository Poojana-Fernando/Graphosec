using Microsoft.Data.Sqlite;
using ResumableCopy.Core.Errors;

namespace ResumableCopy.Core.Storage.Sqlite;

internal static class SqliteDatabaseInitializer
{
    public static void Initialize(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();
            ExecuteNonQuery(connection, transaction, "PRAGMA foreign_keys = ON;");
            SqliteMigrationRunner.Migrate(connection, transaction);
            transaction.Commit();
        }
        catch (SqliteException exception)
        {
            throw SqlitePersistenceExceptionMapper.Map("initialize the transfer session database", exception);
        }
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
