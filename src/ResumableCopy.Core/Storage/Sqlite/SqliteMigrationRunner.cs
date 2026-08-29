using Microsoft.Data.Sqlite;

namespace ResumableCopy.Core.Storage.Sqlite;

internal static class SqliteMigrationRunner
{
    public static void Migrate(SqliteConnection connection, SqliteTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        EnsureVersionTable(connection, transaction);
        var currentVersion = GetCurrentVersion(connection, transaction);

        for (var version = currentVersion + 1; version <= SqliteSchema.CurrentVersion; version++)
        {
            ApplyMigration(connection, transaction, version);
            SetCurrentVersion(connection, transaction, version);
        }
    }

    private static void EnsureVersionTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteNonQuery(connection, transaction, SqliteSchema.CreateSchemaVersionTable);

        if (!HasVersionRow(connection, transaction))
        {
            ExecuteNonQuery(connection, transaction, "INSERT INTO schema_version(version) VALUES (0);");
        }
    }

    private static void ApplyMigration(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        switch (version)
        {
            case 1:
                ExecuteNonQuery(connection, transaction, SqliteSchema.CreateSessionsTable);
                ExecuteNonQuery(connection, transaction, SqliteSchema.CreateChunksTable);
                ExecuteNonQuery(connection, transaction, SqliteSchema.CreateSessionsStateIndex);
                ExecuteNonQuery(connection, transaction, SqliteSchema.CreateChunksSessionStateIndex);
                break;
            case 2:
                AddColumnIfMissing(connection, transaction, "sessions", "source_volume_serial", SqliteSchema.AddSourceVolumeSerialColumn);
                AddColumnIfMissing(connection, transaction, "sessions", "source_file_id", SqliteSchema.AddSourceFileIdColumn);
                break;
            default:
                throw new InvalidOperationException($"Unsupported database migration version {version}.");
        }
    }

    private static void AddColumnIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string alterTableSql)
    {
        if (ColumnExists(connection, transaction, tableName, columnName))
        {
            return;
        }

        ExecuteNonQuery(connection, transaction, alterTableSql);
    }

    private static bool ColumnExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasVersionRow(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(1) FROM schema_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static int GetCurrentVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void SetCurrentVersion(SqliteConnection connection, SqliteTransaction transaction, int version)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE schema_version SET version = $version;";
        command.Parameters.AddWithValue("$version", version);
        command.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
