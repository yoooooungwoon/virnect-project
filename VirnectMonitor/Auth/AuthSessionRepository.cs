using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace VirnectMonitor.Auth;

public sealed class AuthSessionRepository
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public AuthSessionRepository(IOptions<AuthOptions> options, IWebHostEnvironment environment)
    {
        _databasePath = Path.IsPathRooted(options.Value.DatabasePath)
            ? options.Value.DatabasePath
            : Path.Combine(environment.ContentRootPath, options.Value.DatabasePath);

        _connectionString = $"Data Source={_databasePath}";
    }

    public async Task InitializeAsync()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, null, "PRAGMA journal_mode = WAL;");

        await ExecuteAsync(connection, null, """
            CREATE TABLE IF NOT EXISTS auth_sessions (
                id INTEGER PRIMARY KEY,
                token_hash TEXT NOT NULL UNIQUE,
                status TEXT NOT NULL,
                transition_consumed INTEGER NOT NULL DEFAULT 0,
                username TEXT NULL,
                failure_count INTEGER NOT NULL DEFAULT 0,
                client_source TEXT NULL,
                created_at INTEGER NOT NULL,
                login_expires_at INTEGER NOT NULL,
                approved_at INTEGER NULL,
                auth_expires_at INTEGER NULL,
                consumed_at INTEGER NULL,
                failed_at INTEGER NULL,
                revoked_at INTEGER NULL,
                last_checked_at INTEGER NULL
            );
            """);

        await ExecuteAsync(connection, null, """
            CREATE INDEX IF NOT EXISTS ix_auth_sessions_status_approved
            ON auth_sessions(status, approved_at);
            """);
    }

    public async Task<AuthSession> CreatePendingSessionAsync(
        string tokenHash,
        string? clientSource,
        long createdAt,
        long loginExpiresAt)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO auth_sessions (
                token_hash,
                status,
                transition_consumed,
                client_source,
                created_at,
                login_expires_at
            )
            VALUES (
                @token_hash,
                @status,
                0,
                @client_source,
                @created_at,
                @login_expires_at
            );
            SELECT last_insert_rowid();
            """;
        AddParameter(command, "@token_hash", tokenHash);
        AddParameter(command, "@status", AuthStatuses.Pending);
        AddParameter(command, "@client_source", clientSource);
        AddParameter(command, "@created_at", createdAt);
        AddParameter(command, "@login_expires_at", loginExpiresAt);

        var id = (long)(await command.ExecuteScalarAsync() ?? 0L);

        return new AuthSession
        {
            Id = id,
            TokenHash = tokenHash,
            Status = AuthStatuses.Pending,
            TransitionConsumed = false,
            ClientSource = clientSource,
            CreatedAt = createdAt,
            LoginExpiresAt = loginExpiresAt
        };
    }

    public async Task<AuthSession?> GetByTokenHashAsync(string tokenHash)
    {
        await using var connection = await OpenConnectionAsync();
        return await QuerySingleAsync(
            connection,
            null,
            "SELECT * FROM auth_sessions WHERE token_hash = @token_hash LIMIT 1;",
            command => AddParameter(command, "@token_hash", tokenHash));
    }

    public async Task<IReadOnlyList<AuthSession>> ListRecentAsync(int limit)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT *
            FROM auth_sessions
            ORDER BY created_at DESC, id DESC
            LIMIT @limit;
            """;
        AddParameter(command, "@limit", limit);

        var sessions = new List<AuthSession>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sessions.Add(MapSession(reader));
        }

        return sessions;
    }

    public async Task<AuthSession?> GetLatestApprovedAsync()
    {
        await using var connection = await OpenConnectionAsync();
        return await QuerySingleAsync(
            connection,
            null,
            """
            SELECT *
            FROM auth_sessions
            WHERE status = @status
            ORDER BY approved_at DESC, id DESC
            LIMIT 1;
            """,
            command => AddParameter(command, "@status", AuthStatuses.Approved));
    }

    public async Task MarkExpiredAsync(long id, long now)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(
            connection,
            null,
            """
            UPDATE auth_sessions
            SET status = @status,
                last_checked_at = @now
            WHERE id = @id;
            """,
            command =>
            {
                AddParameter(command, "@status", AuthStatuses.Expired);
                AddParameter(command, "@now", now);
                AddParameter(command, "@id", id);
            });
    }

    public async Task<int> ExpireStaleSessionsAsync(long now)
    {
        await using var connection = await OpenConnectionAsync();
        return await ExecuteAsync(
            connection,
            null,
            """
            UPDATE auth_sessions
            SET status = @expired,
                last_checked_at = @now
            WHERE (
                status = @pending
                AND login_expires_at <= @now
            )
            OR (
                status = @approved
                AND auth_expires_at IS NOT NULL
                AND auth_expires_at <= @now
            );
            """,
            command =>
            {
                AddParameter(command, "@expired", AuthStatuses.Expired);
                AddParameter(command, "@now", now);
                AddParameter(command, "@pending", AuthStatuses.Pending);
                AddParameter(command, "@approved", AuthStatuses.Approved);
            });
    }

    public async Task<int> DeleteOldExpiredSessionsAsync(long deleteBefore)
    {
        await using var connection = await OpenConnectionAsync();
        return await ExecuteAsync(
            connection,
            null,
            """
            DELETE FROM auth_sessions
            WHERE status = @expired
              AND COALESCE(auth_expires_at, login_expires_at) <= @delete_before;
            """,
            command =>
            {
                AddParameter(command, "@expired", AuthStatuses.Expired);
                AddParameter(command, "@delete_before", deleteBefore);
            });
    }

    public async Task<int> RevokeActiveSessionsForUserAsync(string username, long exceptSessionId, long now)
    {
        await using var connection = await OpenConnectionAsync();
        return await ExecuteAsync(
            connection,
            null,
            """
            UPDATE auth_sessions
            SET status = @revoked,
                revoked_at = @now,
                last_checked_at = @now
            WHERE username = @username
              AND id <> @except_session_id
              AND status = @approved
              AND auth_expires_at IS NOT NULL
              AND auth_expires_at > @now;
            """,
            command =>
            {
                AddParameter(command, "@revoked", AuthStatuses.Revoked);
                AddParameter(command, "@now", now);
                AddParameter(command, "@username", username);
                AddParameter(command, "@except_session_id", exceptSessionId);
                AddParameter(command, "@approved", AuthStatuses.Approved);
            });
    }

    public async Task<int> RevokeSessionAsync(long id, long now)
    {
        await using var connection = await OpenConnectionAsync();
        return await ExecuteAsync(
            connection,
            null,
            """
            UPDATE auth_sessions
            SET status = @revoked,
                revoked_at = @now,
                last_checked_at = @now
            WHERE id = @id
              AND status IN (@pending, @approved);
            """,
            command =>
            {
                AddParameter(command, "@revoked", AuthStatuses.Revoked);
                AddParameter(command, "@now", now);
                AddParameter(command, "@id", id);
                AddParameter(command, "@pending", AuthStatuses.Pending);
                AddParameter(command, "@approved", AuthStatuses.Approved);
            });
    }

    public async Task ApproveAsync(long id, string username, long now, long authExpiresAt)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(
            connection,
            null,
            """
            UPDATE auth_sessions
            SET status = @status,
                transition_consumed = 0,
                username = @username,
                approved_at = @now,
                auth_expires_at = @auth_expires_at,
                last_checked_at = @now
            WHERE id = @id;
            """,
            command =>
            {
                AddParameter(command, "@status", AuthStatuses.Approved);
                AddParameter(command, "@username", username);
                AddParameter(command, "@now", now);
                AddParameter(command, "@auth_expires_at", authExpiresAt);
                AddParameter(command, "@id", id);
            });
    }

    public async Task RecordFailureAsync(long id, long now, bool lockSession)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(
            connection,
            null,
            """
            UPDATE auth_sessions
            SET failure_count = failure_count + 1,
                status = CASE WHEN @lock_session = 1 THEN @failed ELSE status END,
                failed_at = CASE WHEN @lock_session = 1 THEN @now ELSE failed_at END,
                last_checked_at = @now
            WHERE id = @id;
            """,
            command =>
            {
                AddParameter(command, "@lock_session", lockSession ? 1 : 0);
                AddParameter(command, "@failed", AuthStatuses.Failed);
                AddParameter(command, "@now", now);
                AddParameter(command, "@id", id);
            });
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<AuthSession?> QuerySingleAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        Action<SqliteCommand>? configure = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        configure?.Invoke(command);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapSession(reader) : null;
    }

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        Action<SqliteCommand>? configure = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        configure?.Invoke(command);
        return await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static AuthSession MapSession(SqliteDataReader reader)
    {
        return new AuthSession
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            TokenHash = reader.GetString(reader.GetOrdinal("token_hash")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            TransitionConsumed = reader.GetInt32(reader.GetOrdinal("transition_consumed")) == 1,
            Username = GetNullableString(reader, "username"),
            FailureCount = reader.GetInt32(reader.GetOrdinal("failure_count")),
            ClientSource = GetNullableString(reader, "client_source"),
            CreatedAt = reader.GetInt64(reader.GetOrdinal("created_at")),
            LoginExpiresAt = reader.GetInt64(reader.GetOrdinal("login_expires_at")),
            ApprovedAt = GetNullableInt64(reader, "approved_at"),
            AuthExpiresAt = GetNullableInt64(reader, "auth_expires_at"),
            ConsumedAt = GetNullableInt64(reader, "consumed_at"),
            FailedAt = GetNullableInt64(reader, "failed_at"),
            RevokedAt = GetNullableInt64(reader, "revoked_at"),
            LastCheckedAt = GetNullableInt64(reader, "last_checked_at")
        };
    }

    private static string? GetNullableString(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? null : reader.GetString(index);
    }

    private static long? GetNullableInt64(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? null : reader.GetInt64(index);
    }
}

