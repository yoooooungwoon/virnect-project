// appsettings의 Monitor 섹션을 바인딩하는 설정 모델
namespace VirnectMonitor.Models;

public sealed class MonitorOptions
{
    /// <summary>Prometheus 주소 (도커로 띄운 곳).</summary>
    public string PrometheusUrl { get; set; } = "http://127.0.0.1:9090";

    /// <summary>수집 주기(초). Prometheus scrape 주기와 맞춤.</summary>
    public double IntervalSeconds { get; set; } = 5;

    /// <summary>SQLite 파일 경로.</summary>
    public string DbPath { get; set; } = "monitoring.db";

    /// <summary>서버를 구분하는 라벨 (windows_exporter relabel 결과).</summary>
    public string GroupLabel { get; set; } = "server";

    /// <summary>전체 서버 on/off 판정용 up 메트릭 쿼리 (꺼진 서버도 up=0으로 잡힘).</summary>
    public string UpQuery { get; set; } = "up{job=\"windows-servers\"}";
}
