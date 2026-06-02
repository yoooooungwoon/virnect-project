// 최신 스냅샷과 직전 레벨을 보관하는 메모리 저장소(스레드 안전)
using System.Collections.Concurrent;
using VirnectMonitor.Models;

namespace VirnectMonitor.Services;

public sealed class MonitorStore
{
    // 현재 스냅샷 1벌. 컬렉터가 통째로 교체(읽기는 락 없이 안전).
    private volatile IReadOnlyDictionary<string, ServerSnapshot> _snapshot =
        new Dictionary<string, ServerSnapshot>();

    // (서버, 지표) → 직전 레벨. 레벨 전환(알림) 감지에 사용.
    private readonly ConcurrentDictionary<(string Server, string Metric), Level> _lastLevel = new();

    public IReadOnlyDictionary<string, ServerSnapshot> Snapshot => _snapshot;
    public DateTimeOffset? LastUpdated { get; private set; }
    public string? LastError { get; set; }

    public void Update(IReadOnlyDictionary<string, ServerSnapshot> snapshot, DateTimeOffset updatedAt)
    {
        _snapshot = snapshot;
        LastUpdated = updatedAt;
    }

    public Level GetLastLevel(string server, string metric) =>
        _lastLevel.TryGetValue((server, metric), out var level) ? level : Level.Clean;

    public void SetLastLevel(string server, string metric, Level level) =>
        _lastLevel[(server, metric)] = level;
}
