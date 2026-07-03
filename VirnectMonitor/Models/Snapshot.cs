// API 응답 및 메모리 스냅샷에 쓰는 데이터 모델
namespace VirnectMonitor.Models;

/// <summary>지표 1개의 현재 측정값 + 분류 결과.</summary>
public sealed record MetricReading(
    string Id,
    string Name,
    string Unit,
    double Value,
    string Display,
    string Level,     // clean | warning | danger
    int LevelCode,    // 0 | 1 | 2  (Make&View용)
    string LevelText, // 클린 | 보통 | 위험
    double Warn,
    double Danger)
{
    /// <summary>상태 코드: 1=클린, 0=경고, -1=위험 (= 1 - LevelCode).</summary>
    public int Status => 1 - LevelCode;
}

/// <summary>서버 1대의 전체 지표 스냅샷.</summary>
public sealed record ServerSnapshot(
    string Server,
    IReadOnlyDictionary<string, MetricReading> Metrics,
    string Overall,     // 가장 심각한 레벨 키
    int OverallCode,    // 0 | 1 | 2
    string OverallText, // 클린 | 보통 | 위험
    long UpdatedAt);

/// <summary>DB anomalies 행.</summary>
public sealed record AnomalyRow(
    long Id, string Server, string Metric, string MetricName,
    double Value, string Level, long CreatedAt);

/// <summary>DB alerts 행.</summary>
public sealed record AlertRow(
    long Id, string Server, string Metric, string MetricName,
    double Value, string Level, string? PrevLevel, string Message, long CreatedAt)
{
    /// <summary>레벨 코드: 0=클린, 1=보통(warning), 2=위험(danger).</summary>
    public int LevelCode => Level switch
    {
        "danger" => 2,
        "warning" => 1,
        _ => 0,
    };

    /// <summary>(색이모지) [HH:mm:ss] (지표아이콘) 서버 · 지표명 상태 (값) 형식 한 줄 표시.</summary>
    public string Display
    {
        get
        {
            var color = Level switch
            {
                "danger"  => "🔴",
                "warning" => "🟡",
                _         => "🟢",
            };
            var icon = Metric switch
            {
                "cpu"                    => "⚙️",
                "memory"                 => "💾",
                "disk" or "disk_io"      => "💿",
                "net_recv" or "net_sent" => "🌐",
                _                        => "📊",
            };
            var status = Level switch
            {
                "danger"  => "위험",
                "warning" => "경고",
                _         => "정상 복구",
            };
            var time = DateTimeOffset.FromUnixTimeSeconds(CreatedAt)
                .ToOffset(TimeSpan.FromHours(9))
                .ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            var valueStr = Metrics.ById.TryGetValue(Metric, out var spec)
                ? Metrics.Human(spec, Value)
                : Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            return $"{color} [{time}] {icon} {Server} · {MetricName} {status} ({valueStr})";
        }
    }
}
