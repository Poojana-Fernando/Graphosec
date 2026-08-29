using Microsoft.Data.Sqlite;
using ResumableCopy.Core.Storage.Sqlite;
using ResumableCopy.Core.Tests.TestSupport;

namespace ResumableCopy.Core.Tests;

public class DatabaseMigrationTests
{
    [Fact]
    public void Initialize_NewDatabase_RecordsCurrentSchemaVersion()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "sessions.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

        SqliteDatabaseInitializer.Initialize(connectionString);

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var version = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(SqliteSchema.CurrentVersion, version);
    }

    [Fact]
    public void Initialize_LegacyDatabaseWithoutIdentityColumns_UpgradesAndPreservesData()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "sessions.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

        CreateLegacyVersionOneDatabase(databasePath);

        SqliteDatabaseInitializer.Initialize(connectionString);

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText = "SELECT version FROM schema_version LIMIT 1;";
            var version = Convert.ToInt32(versionCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(SqliteSchema.CurrentVersion, version);
        }

        using (var sessionCommand = connection.CreateCommand())
        {
            sessionCommand.CommandText = """
                SELECT source_size, source_volume_serial, source_file_id
                FROM sessions
                WHERE session_id = 'legacy-session';
                """;

            using var reader = sessionCommand.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(4096L, reader.GetInt64(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
        }
    }

    private static void CreateLegacyVersionOneDatabase(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();

        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, """
            CREATE TABLE schema_version (
                version INTEGER NOT NULL
            );
            """);
        Execute(connection, transaction, "INSERT INTO schema_version(version) VALUES (1);");
        Execute(connection, transaction, """
            CREATE TABLE sessions (
                session_id TEXT PRIMARY KEY NOT NULL,
                source_path TEXT NOT NULL,
                destination_path TEXT NOT NULL,
                staging_path TEXT NOT NULL,
                source_size INTEGER NOT NULL,
                source_last_write_time_utc TEXT NOT NULL,
                source_creation_time_utc TEXT NOT NULL,
                chunk_size INTEGER NOT NULL,
                total_chunks INTEGER NOT NULL,
                state INTEGER NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                last_error TEXT NULL
            );
            """);
        Execute(connection, transaction, """
            INSERT INTO sessions (
                session_id,
                source_path,
                destination_path,
                staging_path,
                source_size,
                source_last_write_time_utc,
                source_creation_time_utc,
                chunk_size,
                total_chunks,
                state,
                created_utc,
                updated_utc,
                last_error)
            VALUES (
                'legacy-session',
                'C:\source\legacy.bin',
                'C:\dest\legacy.bin',
                'C:\dest\.copycache\legacy-session.part',
                4096,
                '2024-01-01T00:00:00.0000000Z',
                '2024-01-01T00:00:00.0000000Z',
                4096,
                1,
                3,
                '2024-01-01T00:00:00.0000000Z',
                '2024-01-01T00:00:00.0000000Z',
                NULL);
            """);
        transaction.Commit();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
