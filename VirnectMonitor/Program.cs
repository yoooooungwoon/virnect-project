// 모니터링 백엔드 진입점 — 수집기 등록 + Make&View/웹 프론트용 GET API 구성
using VirnectMonitor.Models;
using VirnectMonitor.Auth;
using VirnectMonitor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MonitorOptions>(builder.Configuration.GetSection("Monitor"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<PrometheusClient>();
builder.Services.AddSingleton<MonitorStore>();
builder.Services.AddSingleton<MonitorDatabase>();
builder.Services.AddHostedService<CollectorService>();
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<AuthSessionRepository>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<LoginAuditRepository>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHostedService<AuthSessionCleanupService>();

// AR/웹 프론트가 다른 출처에서 GET 호출할 수 있게 CORS 허용
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// DB 초기화(테이블 생성)
await app.Services.GetRequiredService<MonitorDatabase>().InitializeAsync();
await app.Services.GetRequiredService<UserRepository>().InitializeAsync();
await app.Services.GetRequiredService<LoginAuditRepository>().InitializeAsync();
await app.Services.GetRequiredService<AuthSessionRepository>().InitializeAsync();

app.UseCors();
app.Use(async (context, next) =>
{
    if (IsDashboardStaticPath(context.Request.Path))
    {
        var auth = context.RequestServices.GetRequiredService<AuthService>();
        if (!await auth.HasActiveApprovedSessionAsync())
        {
            context.Response.Redirect("/auth/start");
            return;
        }
    }

    await next();
});
app.UseStaticFiles();

app.MapAuthEndpoints();

// 전체 모니터링 (4대) — /server
app.MapGet("/server", async (IWebHostEnvironment environment, AuthService auth) =>
{
    if (!await auth.HasActiveApprovedSessionAsync())
    {
        return Results.Redirect("/auth/start");
    }

    var webRootPath = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    return Results.File(Path.Combine(webRootPath, "index.html"), "text/html");
});

// 서버별 (/server-01 ~ /server-04) — 경로에서 서버명 (server-숫자 형태만 매칭, /login 등과 충돌 없음)
app.MapGet("/{server:regex(^server-\\d+$)}", async (string server, IWebHostEnvironment environment, AuthService auth) =>
{
    if (!await auth.HasActiveApprovedSessionAsync())
    {
        return Results.Redirect("/auth/start");
    }

    var webRootPath = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    return Results.File(Path.Combine(webRootPath, "server.html"), "text/html");
});

// --- 상태/헬스 -------------------------------------------------------
app.MapGet("/api/health", (MonitorStore store, IConfiguration config) => new
{
    ok = store.LastError is null,
    lastUpdated = store.LastUpdated?.ToUnixTimeSeconds(),
    lastUpdatedText = store.LastUpdated is { } t ? FormatKst(t.ToUnixTimeSeconds()) : null,  // 수집시각(한국시간 문자열)
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
    tsText = store.LastUpdated is { } t ? FormatKst(t.ToUnixTimeSeconds()) : null,  // 수집시각(한국시간 문자열)
    servers = store.Snapshot,
});


// 서버 1대 장비요약 — Make&View 연결용으로 필요한 값만 응답. 꺼진 서버도 powercode:0으로 응답.
//  powercode 1=켜짐 0=꺼짐, operationCode 0=전원OFF 1=클린 2=경고 3=위험, alertNum 이상징후 개수
app.MapGet("/api/server/{server}", (string server, MonitorStore store) =>
{
    if (!store.ServerUp.TryGetValue(server, out var up))
        return Results.NotFound(new { error = "서버 없음", server });

    var on = up == 1;
    store.Snapshot.TryGetValue(server, out var s);
    var alertNum = s is null ? 0 : s.Metrics.Values.Count(m => m.LevelCode >= 1);
    var operationCode = ResolveOperationCode(on, s);
    var checkMessage = BuildCheckMessage(on, s);

    return Results.Ok(new
    {
        server,
        powercode = on ? 1 : 0,
        operationCode,
        checkMessage,
        alertNum,
    });
});

// 서버 1대 상세 상태 — 웹 대시보드/상세 모니터링용.
//  각 지표 status: 1=클린 0=경고 -1=위험
app.MapGet("/api/server/{server}/metrics", (string server, MonitorStore store) =>
{
    if (!store.ServerUp.TryGetValue(server, out var up))
        return Results.NotFound(new { error = "서버 없음", server });

    var on = up == 1;
    store.Snapshot.TryGetValue(server, out var s);
    var alertNum = s is null ? 0 : s.Metrics.Values.Count(m => m.LevelCode >= 1);
    long? updated = s?.UpdatedAt ?? store.LastUpdated?.ToUnixTimeSeconds();

    return Results.Ok(new
    {
        server,
        powercode = on ? 1 : 0,
        operationCode = ResolveOperationCode(on, s),
        checkMessage = BuildCheckMessage(on, s),
        alertNum,
        updatedAt = updated,
        updatedAtText = updated is { } u ? FormatKst(u) : null,
        metrics = s?.Metrics ?? new Dictionary<string, MetricReading>(),
    });
});

// --- Make&View 친화: 단일 숫자값 ------------------------------------
// 지표 1개의 현재값 (Make&View가 powercode, display, levelCode만 연결)
//  levelCode 0=전원OFF 1=클린 2=경고 3=위험
app.MapGet("/api/metric/{server}/{metric}", (string server, string metric, MonitorStore store) =>
{
    if (!Metrics.ById.ContainsKey(metric))
        return Results.NotFound(new { error = "지표 없음", metric });

    if (!store.ServerUp.TryGetValue(server, out var up))
        return Results.NotFound(new { error = "서버 없음", server });

    var powercode = up == 1 ? 1 : 0;
    if (powercode == 0)
        return Results.Ok(new { powercode, display = "전원 OFF", levelCode = 0 });

    if (store.Snapshot.TryGetValue(server, out var s) && s.Metrics.TryGetValue(metric, out var m))
        return Results.Ok(new
        {
            powercode,
            display = m.Display,
            levelCode = m.LevelCode + 1,
        });

    return Results.Ok(new { powercode, display = "데이터 없음", levelCode = 2 });
});

// 서버 종합 심각도 숫자 (AR 알림 트리거: {값}>=2 → 위험)
app.MapGet("/api/alert/{server}", (string server, MonitorStore store) =>
    store.Snapshot.TryGetValue(server, out var s)
        ? Results.Ok(new { server, value = s.OverallCode, level = s.Overall, levelText = s.OverallText })
        : Results.NotFound(new { error = "서버 없음", server }));

// 서버별 이상징후 알림 목록 (보통·위험만, 정상 제외, 최대 5개) — AR 알림 표시용
app.MapGet("/api/alert/{server}/issues", (string server, MonitorStore store) =>
{
    if (!store.Snapshot.TryGetValue(server, out var s))
        return Results.NotFound(new { error = "서버 없음", server });

    var alerts = s.Metrics.Values
        .Where(m => m.LevelCode >= 1)               // 클린(0) 제외, 보통(1)·위험(2)만
        .OrderByDescending(m => m.LevelCode)        // 위험 먼저
        .ThenBy(m => m.Id)
        .Select(m => new
        {
            metric = m.Id,
            name = m.Name,
            value = m.Value,
            display = m.Display,
            level = m.Level,
            levelCode = m.LevelCode,
            levelText = m.LevelText,
            unit = m.Unit,
            message = $"{m.Name} {m.Display} {m.LevelText}",
        })
        .ToList();

    return Results.Ok(new
    {
        server,
        updatedAt = s.UpdatedAt,
        updatedAtText = FormatKst(s.UpdatedAt),  // 수집시각(한국시간 문자열)
        count = alerts.Count,
        alerts,
    });
});

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

// --- 차트 이미지(AR용): SVG / PNG 두 방식 ---------------------------
// 차트 엔드포인트 — Make&View가 원격 URL 이미지를 못 띄워 비활성화(주석). 재활성화: 아래 /* */ 해제.
/*
// GET /api/chart/{server}/{metric}?minutes=60&format=svg|png&w=600&h=300
app.MapGet("/api/chart/{server}/{metric}", async (
    string server, string metric, int? minutes, string? format, int? w, int? h,
    PrometheusClient prom, IConfiguration config, CancellationToken ct) =>
{
    if (!Metrics.ById.TryGetValue(metric, out var spec))
        return Results.NotFound(new { error = "지표 없음", metric });

    var groupLabel = config["Monitor:GroupLabel"] ?? "server";
    var windowMin = Math.Clamp(minutes ?? 60, 1, 24 * 60);
    var width = Math.Clamp(w ?? 600, 120, 2000);
    var height = Math.Clamp(h ?? 300, 80, 1200);
    var stepSec = windowMin <= 5 ? 10 : windowMin <= 15 ? 15 : windowMin <= 60 ? 30 : windowMin <= 360 ? 120 : 600;
    var end = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var start = end - windowMin * 60;

    var series = await prom.QueryRangeAsync(spec.Query, start, end, stepSec, ct);
    var match = series.FirstOrDefault(x => x.Labels.TryGetValue(groupLabel, out var v) && v == server);
    var points = match.Points ?? [];

    if (string.Equals(format, "png", StringComparison.OrdinalIgnoreCase))
        return Results.File(ChartRenderer.RenderPng(spec, points, width, height), "image/png");

    return Results.Content(ChartRenderer.RenderSvg(spec, server, points, width, height), "image/svg+xml");
});
*/

// --- 이벤트 이력(SQLite) --------------------------------------------
app.MapGet("/api/anomalies", async (MonitorDatabase db, int? limit, string? server, string? metric, CancellationToken ct) =>
    await db.RecentAnomaliesAsync(Math.Clamp(limit ?? 100, 1, 1000), server, metric, ct));

app.MapGet("/api/alerts", async (MonitorDatabase db, int? limit, string? server, CancellationToken ct) =>
    await db.RecentAlertsAsync(Math.Clamp(limit ?? 50, 1, 500), server, ct));

app.Run();

static bool IsDashboardStaticPath(PathString path)
{
    return path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/server.html", StringComparison.OrdinalIgnoreCase);
}

// Unix초(UTC) → 한국시간 'yyyy-MM-dd HH:mm:ss' 문자열
static string FormatKst(long unixSeconds) =>
    DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
        .ToOffset(TimeSpan.FromHours(9))
        .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

static int ResolveOperationCode(bool powerOn, ServerSnapshot? snapshot)
{
    if (!powerOn)
        return 0;

    if (snapshot is null || snapshot.Metrics.Count == 0)
        return 2;

    return snapshot.OverallCode switch
    {
        >= 2 => 3,
        1 => 2,
        _ => 1,
    };
}

static string BuildCheckMessage(bool powerOn, ServerSnapshot? snapshot)
{
    if (!powerOn)
        return "전원 상태 확인 필요";

    if (snapshot is null || snapshot.Metrics.Count == 0)
        return "수집 데이터 확인 필요";

    var alerts = snapshot.Metrics.Values
        .Where(m => m.LevelCode >= 1)
        .OrderByDescending(m => m.LevelCode)
        .ThenBy(m => m.Id)
        .Select(m => m.Name)
        .ToList();

    if (alerts.Count == 0)
        return "모든 지표 정상";

    var visible = alerts.Take(3).ToList();
    var prefix = string.Join(", ", visible);
    return alerts.Count > visible.Count
        ? $"{prefix}, 외 {alerts.Count - visible.Count}개 확인 필요"
        : $"{prefix} 확인 필요";
}
