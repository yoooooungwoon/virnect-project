// 수집 지표 정의와 클린/보통/위험 분류 로직
using System.Globalization;

namespace VirnectMonitor.Models;

/// <summary>심각도 레벨. 숫자값은 Make&View 조건식(`{값}>N`)에서 그대로 사용한다.</summary>
public enum Level
{
    Clean = 0,   // 클린
    Warning = 1, // 보통
    Danger = 2,  // 위험
}

/// <summary>지표 1개의 정의(PromQL + 임계치)와 분류 규칙.</summary>
public sealed record MetricSpec(string Id, string Name, string Unit, string Query, double Warn, double Danger)
{
    public Level Classify(double value) =>
        value >= Danger ? Level.Danger : value >= Warn ? Level.Warning : Level.Clean;
}

/// <summary>수집할 지표 목록과 표시 헬퍼.</summary>
public static class Metrics
{
    // 모든 쿼리를 by(server)로 집계해 서버당 값 1개를 보장한다.
    // (원래 메모리/디스크 쿼리는 집계가 없었고, 네트워크는 NIC가 여러 개면 값이 여러 개)
    public static readonly IReadOnlyList<MetricSpec> All =
    [
        new("cpu", "CPU 사용률", "%",
            "100 - (avg by(server)(rate(windows_cpu_time_total{mode=\"idle\"}[1m])) * 100)",
            70, 90),
        new("memory", "메모리 사용률", "%",
            "100 - (avg by(server)(windows_memory_available_bytes) / avg by(server)(windows_memory_physical_total_bytes) * 100)",
            70, 90),
        new("disk", "디스크 사용률(C:)", "%",
            "100 - (avg by(server)(windows_logical_disk_free_bytes{volume=\"C:\"}) / avg by(server)(windows_logical_disk_size_bytes{volume=\"C:\"}) * 100)",
            80, 90),
        // windows_net_bytes_total 엔 direction 라벨이 없어(송수신 합계) 방향별 분리 메트릭을 사용
        new("net_recv", "네트워크 수신", "B/s",
            "sum by(server)(rate(windows_net_bytes_received_total{nic!~\".*isatap.*\"}[1m]))",
            50_000_000, 100_000_000),
        new("net_sent", "네트워크 송신", "B/s",
            "sum by(server)(rate(windows_net_bytes_sent_total{nic!~\".*isatap.*\"}[1m]))",
            50_000_000, 100_000_000),
    ];

    public static readonly IReadOnlyDictionary<string, MetricSpec> ById =
        All.ToDictionary(m => m.Id);

    public static string Key(Level level) => level switch
    {
        Level.Danger => "danger",
        Level.Warning => "warning",
        _ => "clean",
    };

    public static string Text(Level level) => level switch
    {
        Level.Danger => "위험",
        Level.Warning => "보통",
        _ => "클린",
    };

    /// <summary>알림 메시지용 사람이 읽기 좋은 값.</summary>
    public static string Human(MetricSpec spec, double value)
    {
        if (spec.Unit == "%")
            return string.Create(CultureInfo.InvariantCulture, $"{value:F1}%");

        if (spec.Unit == "B/s")
        {
            double n = value;
            string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
            int i = 0;
            while (Math.Abs(n) >= 1024 && i < units.Length - 1) { n /= 1024; i++; }
            return string.Create(CultureInfo.InvariantCulture, $"{n:F1} {units[i]}");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:F2}{spec.Unit}");
    }
}
