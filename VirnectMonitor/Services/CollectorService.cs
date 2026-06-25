// 5초마다 Prometheus 조회 → 클린/보통/위험 분류 → 이상징후 기록 → 메모리 스냅샷 갱신
using Microsoft.Extensions.Options;
using VirnectMonitor.Models;

namespace VirnectMonitor.Services;

public sealed class CollectorService(
    PrometheusClient prometheus,
    MonitorStore store,
    MonitorDatabase database,
    DiscordWebhookNotifier discord,
    IOptions<MonitorOptions> options,
    ILogger<CollectorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds));
        var groupLabel = options.Value.GroupLabel;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectOnceAsync(groupLabel, stoppingToken);
                store.LastError = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                store.LastError = ex.Message;
                logger.LogWarning("수집 실패: {Message}", ex.Message);
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CollectOnceAsync(string groupLabel, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        long createdAt = now.ToUnixTimeSeconds();

        // 0) up 메트릭으로 전체 서버 on/off (꺼진 서버도 up=0으로 포함)
        var serverUp = new Dictionary<string, int>();
        foreach (var sample in await prometheus.QueryAsync(options.Value.UpQuery, ct))
        {
            var server = ResolveServer(sample.Labels, groupLabel);
            serverUp[server] = sample.Value >= 0.5 ? 1 : 0;
        }

        // 1) 지표별 쿼리 → server -> (metricId -> value)
        var byServer = new Dictionary<string, Dictionary<string, double>>();
        foreach (var spec in Metrics.All)
        {
            foreach (var sample in await prometheus.QueryAsync(spec.Query, ct))
            {
                var server = ResolveServer(sample.Labels, groupLabel);
                if (!byServer.TryGetValue(server, out var metrics))
                    byServer[server] = metrics = new Dictionary<string, double>();
                metrics[spec.Id] = sample.Value;
            }
        }

        // 2) 분류 + 이상징후 기록 + 레벨 전환 알림 + 스냅샷 구성
        var snapshot = new Dictionary<string, ServerSnapshot>();
        foreach (var (server, values) in byServer)
        {
            var readings = new Dictionary<string, MetricReading>();
            var worst = Level.Clean;

            foreach (var spec in Metrics.All)
            {
                if (!values.TryGetValue(spec.Id, out var value))
                    continue;

                var level = spec.Classify(value);
                if (level > worst) worst = level;

                readings[spec.Id] = new MetricReading(
                    spec.Id, spec.Name, spec.Unit, value, Metrics.Human(spec, value),
                    Metrics.Key(level), (int)level, Metrics.Text(level), spec.Warn, spec.Danger);

                // 클린이 아니면 이상징후로 기록(측정값 + 시간)
                if (level != Level.Clean)
                    await database.InsertAnomalyAsync(
                        server, spec.Id, spec.Name, value, Metrics.Key(level), createdAt, ct);

                // 레벨이 바뀌면 알림 1건(스팸 방지: 전환 시점에만)
                var prev = store.GetLastLevel(server, spec.Id);
                if (level != prev)
                {
                    var message = level == Level.Clean
                        ? $"[{server}] {spec.Name} 정상 복구 ({Metrics.Human(spec, value)})"
                        : $"[{server}] {spec.Name} {Metrics.Text(level)} 상태 ({Metrics.Human(spec, value)})";
                    await database.InsertAlertAsync(
                        server, spec.Id, spec.Name, value, Metrics.Key(level),
                        Metrics.Key(prev), message, createdAt, ct);
                    await discord.SendLevelChangedAsync(server, spec, value, level, prev, createdAt, ct);
                }
                store.SetLastLevel(server, spec.Id, level);
            }

            snapshot[server] = new ServerSnapshot(
                server, readings, Metrics.Key(worst), (int)worst, Metrics.Text(worst), createdAt);
        }

        store.Update(snapshot, serverUp, now);
        logger.LogInformation("수집 완료: 켜진 서버 {On}대 / 전체 {Total}대",
            serverUp.Count(kv => kv.Value == 1), serverUp.Count);
    }

    private static string ResolveServer(IReadOnlyDictionary<string, string> labels, string groupLabel)
    {
        if (labels.TryGetValue(groupLabel, out var s) && !string.IsNullOrEmpty(s)) return s;
        if (labels.TryGetValue("instance", out var inst) && !string.IsNullOrEmpty(inst)) return inst;
        return "unknown";
    }
}
