using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace VirnectMonitor.Auth;

public sealed class LoginAuditRepository
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public LoginAuditRepository(IOptions<AuthOptions> options, IWebHostEnvironment environment)
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
        await ExecuteAsync(connection, null, """
            CREATE TABLE IF NOT EXISTS login_audit_events (
                id INTEGER PRIMARY KEY,
                username TEXT NULL,
                username_normalized TEXT NULL,
                result TEXT NOT NULL,
                reason TEXT NULL,
                session_id INTEGER NULL,
                client_ip TEXT NULL,
                user_agent TEXT NULL,
                occurred_at INTEGER NOT NULL
            );
            """);

        await ExecuteAsync(connection, null, """
            CREATE INDEX IF NOT EXISTS ix_login_audit_events_occurred
            ON login_audit_events(occurred_at);
            """);
    }

    public async Task RecordAsync(
        string? username,
        string result,
        string? reason,
        long? sessionId,
        string? clientIp,
        string? userAgent,
        long occurredAt)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(
            connection,
            null,
            """
            INSERT INTO login_audit_events (
                username,
                username_normalized,
                result,
                reason,
                session_id,
                client_ip,
                user_agent,
                occurred_at
            )
            VALUES (
                @username,
                @username_normalized,
                @result,
                @reason,
                @session_id,
                @client_ip,
                @user_agent,
                @occurred_at
            );
            """,
            command =>
            {
                AddParameter(command, "@username", string.IsNullOrWhiteSpace(username) ? null : username.Trim());
                AddParameter(command, "@username_normalized", string.IsNullOrWhiteSpace(username) ? null : UserRepository.NormalizeUsername(username));
                AddParameter(command, "@result", result);
                AddParameter(command, "@reason", reason);
                AddParameter(command, "@session_id", sessionId);
                AddParameter(command, "@client_ip", clientIp);
                AddParameter(command, "@user_agent", userAgent);
                AddParameter(command, "@occurred_at", occurredAt);
            });
    }

    public async Task<IReadOnlyList<LoginAuditEvent>> ListRecentAsync(int limit)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT *
            FROM login_audit_events
            ORDER BY occurred_at DESC, id DESC
            LIMIT @limit;
            """;
        AddParameter(command, "@limit", limit);

        var events = new List<LoginAuditEvent>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(MapEvent(reader));
        }

        return events;
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
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

    private static LoginAuditEvent MapEvent(SqliteDataReader reader)
    {
        return new LoginAuditEvent
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Username = GetNullableString(reader, "username"),
            UsernameNormalized = GetNullableString(reader, "username_normalized"),
            Result = reader.GetString(reader.GetOrdinal("result")),
            Reason = GetNullableString(reader, "reason"),
            SessionId = GetNullableInt64(reader, "session_id"),
            ClientIp = GetNullableString(reader, "client_ip"),
            UserAgent = GetNullableString(reader, "user_agent"),
            OccurredAt = reader.GetInt64(reader.GetOrdinal("occurred_at"))
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

