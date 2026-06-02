// 모니터링 백엔드 진입점 — 수집기 등록 + Make&View/웹 프론트용 GET API 구성
using VirnectMonitor.Models;
using VirnectMonitor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MonitorOptions>(builder.Configuration.GetSection("Monitor"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<PrometheusClient>();
builder.Services.AddSingleton<MonitorStore>();
builder.Services.AddSingleton<MonitorDatabase>();
builder.Services.AddHostedService<CollectorService>();

// AR/웹 프론트가 다른 출처에서 GET 호출할 수 있게 CORS 허용
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// DB 초기화(테이블 생성)
await app.Services.GetRequiredService<MonitorDatabase>().InitializeAsync();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// --- 상태/헬스 -------------------------------------------------------
app.MapGet("/api/health", (MonitorStore store, IConfiguration config) => new
{
    ok = store.LastError is null,
    lastUpdated = store.LastUpdated?.ToUnixTimeSeconds(),
    lastError = store.LastError,
    prometheusUrl = config["Monitor:PrometheusUrl"],
    servers = store.Snapshot.Keys.OrderBy(x => x),
});

// 지표 정의/임계치 (프론트 표시용)
app.MapGet("/api/metrics", () => new
{
    levels = new { clean = "클린", warning = "보통", danger = "위험" },
    metrics = Metrics.All.Select(m => new { m.Id, m.Name, m.Unit, m.Warn, m.Danger }),
});

// 모든 서버 현재 상태 스냅샷 (웹 대시보드 메인)
app.MapGet("/api/status", (MonitorStore store) => new
{
    ts = store.LastUpdated?.ToUnixTimeSeconds(),
    servers = store.Snapshot,
});

// 서버 1대 현재 상태
app.MapGet("/api/server/{server}", (string server, MonitorStore store) =>
    store.Snapshot.TryGetValue(server, out var s)
        ? Results.Ok(s)
        : Results.NotFound(new { error = "서버 없음", server }));

// --- Make&View 친화: 단일 숫자값 ------------------------------------
// 지표 1개의 현재값 (AR이 value 읽어 표시)
app.MapGet("/api/metric/{server}/{metric}", (string server, string metric, MonitorStore store) =>
{
    if (store.Snapshot.TryGetValue(server, out var s) && s.Metrics.TryGetValue(metric, out var m))
        return Results.Ok(new
        {
            server, metric, name = m.Name, value = m.Value, display = m.Display,
            level = m.Level, levelCode = m.LevelCode, levelText = m.LevelText, unit = m.Unit,
        });
    return Results.NotFound(new { error = "데이터 없음", server, metric });
});

// 서버 종합 심각도 숫자 (AR 알림 트리거: {값}>=2 → 위험)
app.MapGet("/api/alert/{server}", (string server, MonitorStore store) =>
    store.Snapshot.TryGetValue(server, out var s)
        ? Results.Ok(new { server, value = s.OverallCode, level = s.Overall, levelText = s.OverallText })
        : Results.NotFound(new { error = "서버 없음", server }));

// 전체 종합(가장 심각한 서버 기준)
app.MapGet("/api/alert", (MonitorStore store) =>
{
    var worst = store.Snapshot.Values.Select(v => v.OverallCode).DefaultIfEmpty(0).Max();
    return new
    {
        value = worst,
        servers = store.Snapshot.Values.Select(v => new { v.Server, v.OverallCode, v.Overall }),
    };
});

// --- 시계열(웹 그래프): Prometheus range query 프록시 ----------------
app.MapGet("/api/history/{server}/{metric}", async (
    string server, string metric, int? minutes, int? step,
    PrometheusClient prom, IConfiguration config, CancellationToken ct) =>
{
    if (!Metrics.ById.TryGetValue(metric, out var spec))
        return Results.NotFound(new { error = "지표 없음", metric });

    var groupLabel = config["Monitor:GroupLabel"] ?? "server";
    var windowMin = Math.Clamp(minutes ?? 60, 1, 24 * 60);
    var stepSec = Math.Clamp(step ?? 15, 5, 3600);
    var end = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var start = end - windowMin * 60;

    var series = await prom.QueryRangeAsync(spec.Query, start, end, stepSec, ct);
    var match = series.FirstOrDefault(x =>
        x.Labels.TryGetValue(groupLabel, out var v) && v == server);

    var points = (match.Points ?? []).Select(p => new object[] { p.Ts, p.Value });
    return Results.Ok(new { server, metric, unit = spec.Unit, name = spec.Name, points });
});

// 한 지표의 "모든 서버" 추이를 한 번에 (대시보드 시계열 그래프용)
app.MapGet("/api/history/{metric}", async (
    string metric, int? minutes, int? step,
    PrometheusClient prom, IConfiguration config, CancellationToken ct) =>
{
    if (!Metrics.ById.TryGetValue(metric, out var spec))
        return Results.NotFound(new { error = "지표 없음", metric });

    var groupLabel = config["Monitor:GroupLabel"] ?? "server";
    var windowMin = Math.Clamp(minutes ?? 60, 1, 24 * 60);
    var stepSec = Math.Clamp(step ?? 15, 5, 3600);
    var end = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var start = end - windowMin * 60;

    var series = await prom.QueryRangeAsync(spec.Query, start, end, stepSec, ct);
    var result = series
        .Select(s => new
        {
            server = s.Labels.TryGetValue(groupLabel, out var v) ? v : "unknown",
            points = s.Points.Select(p => new object[] { p.Ts, p.Value }),
        })
        .OrderBy(x => x.server);
    return Results.Ok(new { metric, unit = spec.Unit, name = spec.Name, series = result });
});

// --- 이벤트 이력(SQLite) --------------------------------------------
app.MapGet("/api/anomalies", async (MonitorDatabase db, int? limit, string? server, string? metric, CancellationToken ct) =>
    await db.RecentAnomaliesAsync(Math.Clamp(limit ?? 100, 1, 1000), server, metric, ct));

app.MapGet("/api/alerts", async (MonitorDatabase db, int? limit, CancellationToken ct) =>
    await db.RecentAlertsAsync(Math.Clamp(limit ?? 50, 1, 500), ct));

app.Run();
