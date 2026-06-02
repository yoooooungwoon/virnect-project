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
    double Danger);

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
    double Value, string Level, string? PrevLevel, string Message, long CreatedAt);
