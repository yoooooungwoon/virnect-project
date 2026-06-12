using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace VirnectMonitor.Auth;

public sealed class UserRepository
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public UserRepository(IOptions<AuthOptions> options, IWebHostEnvironment environment)
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
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY,
                username TEXT NOT NULL,
                username_normalized TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                role TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                last_login_at INTEGER NULL
            );
            """);
    }

    public async Task<bool> HasAnyUserAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM users LIMIT 1);";
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) == 1;
    }

    public async Task<UserAccount?> GetByUsernameAsync(string username)
    {
        await using var connection = await OpenConnectionAsync();
        return await QuerySingleAsync(
            connection,
            null,
            "SELECT * FROM users WHERE username_normalized = @username_normalized LIMIT 1;",
            command => AddParameter(command, "@username_normalized", NormalizeUsername(username)));
    }

    public async Task<UserAccount> CreateFirstAdminAsync(string username, string passwordHash, long now)
    {
        var normalized = NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Username is required.", nameof(username));
        }

        await using var connection = await OpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        var hasUser = await ScalarIntAsync(
            connection,
            transaction,
            "SELECT EXISTS(SELECT 1 FROM users LIMIT 1);");

        if (hasUser == 1)
        {
            transaction.Commit();
            throw new InvalidOperationException("Initial admin account already exists.");
        }

        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO users (
                username,
                username_normalized,
                password_hash,
                role,
                status,
                created_at
            )
            VALUES (
                @username,
                @username_normalized,
                @password_hash,
                @role,
                @status,
                @created_at
            );
            """,
            command =>
            {
                AddParameter(command, "@username", username.Trim());
                AddParameter(command, "@username_normalized", normalized);
                AddParameter(command, "@password_hash", passwordHash);
                AddParameter(command, "@role", UserRoles.Admin);
                AddParameter(command, "@status", UserStatuses.Active);
                AddParameter(command, "@created_at", now);
            });

        var id = await ScalarLongAsync(connection, transaction, "SELECT last_insert_rowid();");
        transaction.Commit();

        return new UserAccount
        {
            Id = id,
            Username = username.Trim(),
            UsernameNormalized = normalized,
            PasswordHash = passwordHash,
            Role = UserRoles.Admin,
            Status = UserStatuses.Active,
            CreatedAt = now
        };
    }

    public async Task UpdateLastLoginAsync(long id, long now)
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(
            connection,
            null,
            "UPDATE users SET last_login_at = @now WHERE id = @id;",
            command =>
            {
                AddParameter(command, "@now", now);
                AddParameter(command, "@id", id);
            });
    }

    public static string NormalizeUsername(string username)
    {
        return username.Trim().ToUpperInvariant();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<UserAccount?> QuerySingleAsync(
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
        return await reader.ReadAsync() ? MapUser(reader) : null;
    }

    private static async Task<int> ScalarIntAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<long> ScalarLongAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
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

    private static UserAccount MapUser(SqliteDataReader reader)
    {
        return new UserAccount
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Username = reader.GetString(reader.GetOrdinal("username")),
            UsernameNormalized = reader.GetString(reader.GetOrdinal("username_normalized")),
            PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
            Role = reader.GetString(reader.GetOrdinal("role")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            CreatedAt = reader.GetInt64(reader.GetOrdinal("created_at")),
            LastLoginAt = GetNullableInt64(reader, "last_login_at")
        };
    }

    private static long? GetNullableInt64(SqliteDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? null : reader.GetInt64(index);
    }
}

