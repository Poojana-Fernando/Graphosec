using Microsoft.Data.Sqlite;
using ResumableCopy.Core.Abstractions;
using ResumableCopy.Core.Core;
using ResumableCopy.Core.Domain;
using ResumableCopy.Core.Errors;
using ResumableCopy.Core.Storage.Sqlite;

namespace ResumableCopy.Core.Storage;

/// <summary>
/// SQLite-backed session repository scoped to a single destination <c>.copycache</c> directory.
/// </summary>
/// <remarks>
/// <para>
/// The SQLite database and staging <c>.part</c> file are separate persistence systems.
/// There is no distributed transaction between the filesystem and SQLite. If a staging write
/// succeeds but the database transaction does not commit, the chunk must be treated as
/// uncommitted and re-verified during Phase 3 recovery. If the database marks a chunk as
/// verified but staging bytes are later found invalid, the chunk must be revalidated rather
/// than blindly trusted.
/// </para>
/// <para>
/// Source identity persisted here uses Phase 1 metadata (size and timestamps) only.
/// It is not yet a cryptographically strong or Windows FileId-based identity.
/// </para>
/// </remarks>
public sealed class SqliteSessionRepository : ISessionRepository, IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteSessionRepositoryOptions _options;
    private readonly object _sync = new();
    private bool _disposed;

    public SqliteSessionRepository(string cacheDirectory, SqliteSessionRepositoryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        Directory.CreateDirectory(cacheDirectory);
        var databasePath = Path.Combine(cacheDirectory, SqliteSchema.DatabaseFileName);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        _options = options ?? new SqliteSessionRepositoryOptions();

        SqliteDatabaseInitializer.Initialize(_connectionString);
    }

    public string DatabasePath => new SqliteConnectionStringBuilder(_connectionString).DataSource;

    public ValueTask<CopySession?> FindAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        lock (_sync)
        {
            EnsureNotDisposed();
            return ValueTask.FromResult(FindSessionInternal(sessionId));
        }
    }

    public ValueTask<IReadOnlyList<CopySession>> FindUnfinishedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            EnsureNotDisposed();

            var sessionIds = QuerySessionIds(
                """
                SELECT session_id
                FROM sessions
                WHERE state NOT IN (@completedState, @cancelledState)
                ORDER BY created_utc;
                """,
                command =>
                {
                    command.Parameters.AddWithValue("@completedState", (int)CopyState.Completed);
                    command.Parameters.AddWithValue("@cancelledState", (int)CopyState.Cancelled);
                });

            var sessions = sessionIds
                .Select(FindSessionInternal)
                .Where(static session => session is not null)
                .Cast<CopySession>()
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<CopySession>>(sessions);
        }
    }

    public ValueTask SaveAsync(CopySession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(session);

        lock (_sync)
        {
            EnsureNotDisposed();

            session.UpdatedAtUtc = DateTimeOffset.UtcNow;

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            UpsertSession(connection, transaction, session);
            transaction.Commit();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkChunkCompleteAsync(string sessionId, ChunkRecord chunk, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(chunk);

        if (chunk.Hash is null || chunk.Hash.Length == 0)
        {
            throw new InvalidOperationException("Verified chunks must include a hash before persistence.");
        }

        lock (_sync)
        {
            EnsureNotDisposed();

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            var now = DateTimeOffset.UtcNow;

            InsertChunkIfMissing(connection, transaction, sessionId, chunk);

            using (var updateChunk = connection.CreateCommand())
            {
                updateChunk.Transaction = transaction;
                updateChunk.CommandText = """
                    UPDATE chunks
                    SET expected_hash = @expectedHash,
                        state = @verifiedState,
                        last_verified_utc = @lastVerifiedUtc
                    WHERE session_id = @sessionId
                      AND chunk_index = @chunkIndex;
                    """;
                updateChunk.Parameters.AddWithValue("@expectedHash", chunk.Hash);
                updateChunk.Parameters.AddWithValue("@verifiedState", (int)ChunkPersistenceState.Verified);
                updateChunk.Parameters.AddWithValue("@lastVerifiedUtc", now.ToString("O"));
                updateChunk.Parameters.AddWithValue("@sessionId", sessionId);
                updateChunk.Parameters.AddWithValue("@chunkIndex", chunk.Index);

                var rowsAffected = updateChunk.ExecuteNonQuery();
                if (rowsAffected == 0)
                {
                    throw new InvalidOperationException($"Chunk {chunk.Index} was not found in session '{sessionId}'.");
                }
            }

            using (var updateSession = connection.CreateCommand())
            {
                updateSession.Transaction = transaction;
                updateSession.CommandText = """
                    UPDATE sessions
                    SET updated_utc = @updatedUtc
                    WHERE session_id = @sessionId;
                    """;
                updateSession.Parameters.AddWithValue("@updatedUtc", now.ToString("O"));
                updateSession.Parameters.AddWithValue("@sessionId", sessionId);
                updateSession.ExecuteNonQuery();
            }

            _options.BeforeCommit?.Invoke(transaction);

            var faultContext = new Reliability.FaultContext
            {
                SessionId = sessionId,
                ChunkIndex = chunk.Index,
                AttemptNumber = 0
            };
            _options.FaultInjector?.Apply(Reliability.FaultPoint.BeforeDatabaseCommit, faultContext);
            transaction.Commit();
            _options.FaultInjector?.Apply(Reliability.FaultPoint.AfterDatabaseCommit, faultContext);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkChunkPendingAsync(string sessionId, ChunkRecord chunk, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(chunk);

        lock (_sync)
        {
            EnsureNotDisposed();

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            InsertChunkIfMissing(connection, transaction, sessionId, chunk);

            using (var updateChunk = connection.CreateCommand())
            {
                updateChunk.Transaction = transaction;
                updateChunk.CommandText = """
                    UPDATE chunks
                    SET expected_hash = NULL,
                        state = @pendingState,
                        last_verified_utc = NULL
                    WHERE session_id = @sessionId
                      AND chunk_index = @chunkIndex;
                    """;
                updateChunk.Parameters.AddWithValue("@pendingState", (int)ChunkPersistenceState.Pending);
                updateChunk.Parameters.AddWithValue("@sessionId", sessionId);
                updateChunk.Parameters.AddWithValue("@chunkIndex", chunk.Index);

                var rowsAffected = updateChunk.ExecuteNonQuery();
                if (rowsAffected == 0)
                {
                    throw new InvalidOperationException($"Chunk {chunk.Index} was not found in session '{sessionId}'.");
                }
            }

            using (var updateSession = connection.CreateCommand())
            {
                updateSession.Transaction = transaction;
                updateSession.CommandText = """
                    UPDATE sessions
                    SET updated_utc = @updatedUtc
                    WHERE session_id = @sessionId;
                    """;
                updateSession.Parameters.AddWithValue("@updatedUtc", DateTimeOffset.UtcNow.ToString("O"));
                updateSession.Parameters.AddWithValue("@sessionId", sessionId);
                updateSession.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        lock (_sync)
        {
            EnsureNotDisposed();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM sessions WHERE session_id = @sessionId;";
            command.Parameters.AddWithValue("@sessionId", sessionId);
            command.ExecuteNonQuery();
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        _disposed = true;
    }

    private CopySession? FindSessionInternal(string sessionId)
    {
        using var connection = OpenConnection();

        CopySession? session;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT session_id,
                       source_path,
                       destination_path,
                       staging_path,
                       source_size,
                       source_last_write_time_utc,
                       source_creation_time_utc,
                       source_volume_serial,
                       source_file_id,
                       chunk_size,
                       total_chunks,
                       state,
                       created_utc,
                       updated_utc,
                       last_error
                FROM sessions
                WHERE session_id = @sessionId;
                """;
            command.Parameters.AddWithValue("@sessionId", sessionId);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            session = ReadSession(reader);
        }

        session!.Chunks = ChunkPlanMerger.Merge(session, LoadChunks(connection, sessionId));
        return session;
    }

    private static CopySession ReadSession(SqliteDataReader reader)
    {
        var volumeSerial = reader.IsDBNull(7) ? (ulong?)null : (ulong)reader.GetInt64(7);
        var fileId = reader.IsDBNull(8) ? (ulong?)null : (ulong)reader.GetInt64(8);

        return new CopySession
        {
            SessionId = reader.GetString(0),
            SourcePath = reader.GetString(1),
            DestinationPath = reader.GetString(2),
            StagingPath = reader.GetString(3),
            SourceIdentity = new SourceIdentity(
                reader.GetInt64(4),
                DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
                volumeSerial,
                fileId),
            ChunkSize = reader.GetInt32(9),
            TotalChunks = reader.GetInt32(10),
            State = (CopyState)reader.GetInt32(11),
            CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(12)),
            UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(13)),
            LastError = reader.IsDBNull(14) ? null : reader.GetString(14)
        };
    }

    private static List<ChunkRecord> LoadChunks(SqliteConnection connection, string sessionId)
    {
        var chunks = new List<ChunkRecord>();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT chunk_index,
                   chunk_offset,
                   chunk_length,
                   expected_hash,
                   state
            FROM chunks
            WHERE session_id = @sessionId
            ORDER BY chunk_index;
            """;
        command.Parameters.AddWithValue("@sessionId", sessionId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            byte[]? hash = reader.IsDBNull(3) ? null : (byte[])reader.GetValue(3);
            var state = (ChunkPersistenceState)reader.GetInt32(4);

            chunks.Add(new ChunkRecord
            {
                Index = reader.GetInt32(0),
                Offset = reader.GetInt64(1),
                Length = reader.GetInt32(2),
                Hash = hash,
                IsComplete = state == ChunkPersistenceState.Verified
            });
        }

        return chunks;
    }

    private static void UpsertSession(SqliteConnection connection, SqliteTransaction transaction, CopySession session)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sessions (
                session_id,
                source_path,
                destination_path,
                staging_path,
                source_size,
                source_last_write_time_utc,
                source_creation_time_utc,
                source_volume_serial,
                source_file_id,
                chunk_size,
                total_chunks,
                state,
                created_utc,
                updated_utc,
                last_error)
            VALUES (
                @sessionId,
                @sourcePath,
                @destinationPath,
                @stagingPath,
                @sourceSize,
                @sourceLastWriteTimeUtc,
                @sourceCreationTimeUtc,
                @sourceVolumeSerial,
                @sourceFileId,
                @chunkSize,
                @totalChunks,
                @state,
                @createdUtc,
                @updatedUtc,
                @lastError)
            ON CONFLICT(session_id) DO UPDATE SET
                staging_path = excluded.staging_path,
                source_size = excluded.source_size,
                source_last_write_time_utc = excluded.source_last_write_time_utc,
                source_creation_time_utc = excluded.source_creation_time_utc,
                source_volume_serial = excluded.source_volume_serial,
                source_file_id = excluded.source_file_id,
                chunk_size = excluded.chunk_size,
                total_chunks = excluded.total_chunks,
                state = excluded.state,
                updated_utc = excluded.updated_utc,
                last_error = excluded.last_error;
            """;

        command.Parameters.AddWithValue("@sessionId", session.SessionId);
        command.Parameters.AddWithValue("@sourcePath", session.SourcePath);
        command.Parameters.AddWithValue("@destinationPath", session.DestinationPath);
        command.Parameters.AddWithValue("@stagingPath", session.StagingPath);
        command.Parameters.AddWithValue("@sourceSize", session.SourceIdentity.Length);
        command.Parameters.AddWithValue("@sourceLastWriteTimeUtc", session.SourceIdentity.LastWriteTimeUtc.ToString("O"));
        command.Parameters.AddWithValue("@sourceCreationTimeUtc", session.SourceIdentity.CreationTimeUtc.ToString("O"));
        command.Parameters.AddWithValue("@sourceVolumeSerial", (object?)session.SourceIdentity.VolumeSerial ?? DBNull.Value);
        command.Parameters.AddWithValue("@sourceFileId", (object?)session.SourceIdentity.FileId ?? DBNull.Value);
        command.Parameters.AddWithValue("@chunkSize", session.ChunkSize);
        command.Parameters.AddWithValue("@totalChunks", session.TotalChunks);
        command.Parameters.AddWithValue("@state", (int)session.State);
        command.Parameters.AddWithValue("@createdUtc", session.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("@updatedUtc", session.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("@lastError", (object?)session.LastError ?? DBNull.Value);

        command.ExecuteNonQuery();
    }

    private static void InsertChunkIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sessionId,
        ChunkRecord chunk)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO chunks (
                session_id,
                chunk_index,
                chunk_offset,
                chunk_length,
                expected_hash,
                state,
                last_verified_utc)
            VALUES (
                @sessionId,
                @chunkIndex,
                @chunkOffset,
                @chunkLength,
                @expectedHash,
                @state,
                @lastVerifiedUtc);
            """;

        command.Parameters.AddWithValue("@sessionId", sessionId);
        command.Parameters.AddWithValue("@chunkIndex", chunk.Index);
        command.Parameters.AddWithValue("@chunkOffset", chunk.Offset);
        command.Parameters.AddWithValue("@chunkLength", chunk.Length);
        command.Parameters.AddWithValue("@expectedHash", (object?)chunk.Hash ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@state",
            chunk.IsComplete ? (int)ChunkPersistenceState.Verified : (int)ChunkPersistenceState.Pending);
        command.Parameters.AddWithValue("@lastVerifiedUtc", DBNull.Value);

        command.ExecuteNonQuery();
    }

    private List<string> QuerySessionIds(string sql, Action<SqliteCommand>? configure = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure?.Invoke(command);

        var sessionIds = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sessionIds.Add(reader.GetString(0));
        }

        return sessionIds;
    }

    private SqliteConnection OpenConnection()
    {
        try
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch (SqliteException exception)
        {
            throw SqlitePersistenceExceptionMapper.Map("open the transfer session database", exception);
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
