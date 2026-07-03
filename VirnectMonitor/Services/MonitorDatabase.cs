// SQLite 저장 계층 — 이상징후(anomalies)와 알림(alerts) 이벤트 기록
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using VirnectMonitor.Models;

namespace VirnectMonitor.Services;

public sealed class MonitorDatabase(IOptions<MonitorOptions> options)
{
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder { DataSource = options.Value.DbPath }.ToString();

    public async Task InitializeAsync()
    {
        await using var conn = await OpenAsync(default);
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS anomalies (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                server      TEXT    NOT NULL,
                metric      TEXT    NOT NULL,
                metric_name TEXT    NOT NULL,
                value       REAL    NOT NULL,
                level       TEXT    NOT NULL,
                created_at  INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_anomalies_time   ON anomalies(created_at);
            CREATE INDEX IF NOT EXISTS idx_anomalies_server ON anomalies(server);

            CREATE TABLE IF NOT EXISTS alerts (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                server       TEXT    NOT NULL,
                metric       TEXT    NOT NULL,
                metric_name  TEXT    NOT NULL,
                value        REAL    NOT NULL,
                level        TEXT    NOT NULL,
                prev_level   TEXT,
                message      TEXT    NOT NULL,
                created_at   INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_alerts_time ON alerts(created_at);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InsertAnomalyAsync(
        string server, string metric, string metricName, double value, string level, long createdAt, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO anomalies(server, metric, metric_name, value, level, created_at) VALUES ($s,$m,$mn,$v,$l,$t)";
        Bind(cmd, "$s", server); Bind(cmd, "$m", metric); Bind(cmd, "$mn", metricName);
        Bind(cmd, "$v", value); Bind(cmd, "$l", level); Bind(cmd, "$t", createdAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task InsertAlertAsync(
        string server, string metric, string metricName, double value, string level,
        string? prevLevel, string message, long createdAt, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO alerts(server, metric, metric_name, value, level, prev_level, message, created_at) " +
            "VALUES ($s,$m,$mn,$v,$l,$pl,$msg,$t)";
        Bind(cmd, "$s", server); Bind(cmd, "$m", metric); Bind(cmd, "$mn", metricName);
        Bind(cmd, "$v", value); Bind(cmd, "$l", level); Bind(cmd, "$pl", (object?)prevLevel ?? DBNull.Value);
        Bind(cmd, "$msg", message); Bind(cmd, "$t", createdAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<AnomalyRow>> RecentAnomaliesAsync(
        int limit, string? server, string? metric, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, server, metric, metric_name, value, level, created_at FROM anomalies " +
            "WHERE ($s IS NULL OR server = $s) AND ($m IS NULL OR metric = $m) " +
            "ORDER BY created_at DESC LIMIT $lim";
        Bind(cmd, "$s", (object?)server ?? DBNull.Value);
        Bind(cmd, "$m", (object?)metric ?? DBNull.Value);
        Bind(cmd, "$lim", limit);

        var rows = new List<AnomalyRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(new AnomalyRow(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetDouble(4), r.GetString(5), r.GetInt64(6)));
        return rows;
    }

    public async Task<List<AlertRow>> RecentAlertsAsync(int limit, string? server, string? level, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, server, metric, metric_name, value, level, prev_level, created_at FROM alerts " +
            "WHERE ($s IS NULL OR server = $s) " +
            "AND (($lvl IS NULL AND level <> 'clean') OR ($lvl IS NOT NULL AND level = $lvl)) " +
            "ORDER BY created_at DESC LIMIT $lim";
        Bind(cmd, "$s", (object?)server ?? DBNull.Value);
        Bind(cmd, "$lvl", (object?)level ?? DBNull.Value);
        Bind(cmd, "$lim", limit);

        var rows = new List<AlertRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(new AlertRow(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetDouble(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                Metrics.AlertMessage(r.GetString(2), r.GetString(3), r.GetDouble(4), r.GetString(5), r.GetInt64(7)),
                r.GetInt64(7)));
        return rows;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static void Bind(SqliteCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
