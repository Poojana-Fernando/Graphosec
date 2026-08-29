namespace ResumableCopy.Core.Storage.Sqlite;

internal static class SqliteSchema
{
    public const int CurrentVersion = 2;

    public const string DatabaseFileName = "sessions.db";

    public const string CreateSchemaVersionTable = """
        CREATE TABLE IF NOT EXISTS schema_version (
            version INTEGER NOT NULL
        );
        """;

    public const string CreateSessionsTable = """
        CREATE TABLE IF NOT EXISTS sessions (
            session_id TEXT PRIMARY KEY NOT NULL,
            source_path TEXT NOT NULL,
            destination_path TEXT NOT NULL,
            staging_path TEXT NOT NULL,
            source_size INTEGER NOT NULL,
            source_last_write_time_utc TEXT NOT NULL,
            source_creation_time_utc TEXT NOT NULL,
            source_volume_serial INTEGER NULL,
            source_file_id INTEGER NULL,
            chunk_size INTEGER NOT NULL,
            total_chunks INTEGER NOT NULL,
            state INTEGER NOT NULL,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            last_error TEXT NULL
        );
        """;

    public const string CreateChunksTable = """
        CREATE TABLE IF NOT EXISTS chunks (
            session_id TEXT NOT NULL,
            chunk_index INTEGER NOT NULL,
            chunk_offset INTEGER NOT NULL,
            chunk_length INTEGER NOT NULL,
            expected_hash BLOB NULL,
            state INTEGER NOT NULL,
            last_verified_utc TEXT NULL,
            PRIMARY KEY (session_id, chunk_index),
            FOREIGN KEY (session_id) REFERENCES sessions(session_id) ON DELETE CASCADE
        );
        """;

    public const string CreateSessionsStateIndex = """
        CREATE INDEX IF NOT EXISTS idx_sessions_state ON sessions(state);
        """;

    public const string CreateChunksSessionStateIndex = """
        CREATE INDEX IF NOT EXISTS idx_chunks_session_state ON chunks(session_id, state);
        """;

    public const string AddSourceVolumeSerialColumn = """
        ALTER TABLE sessions ADD COLUMN source_volume_serial INTEGER NULL;
        """;

    public const string AddSourceFileIdColumn = """
        ALTER TABLE sessions ADD COLUMN source_file_id INTEGER NULL;
        """;
}
