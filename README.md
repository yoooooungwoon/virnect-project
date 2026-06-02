# virnect-project — 서버 모니터링 백엔드 (moni-back)

Prometheus(windows_exporter)가 수집한 **서버 4대**(server-01~04)의 지표를 **5초마다** 가져와
**클린 / 보통 / 위험** 3단계로 분류하고, 이상징후를 DB에 기록하며,
**Make&View(AR)** 와 **웹 대시보드** 두 소비자에게 JSON을 제공하는 C# / ASP.NET Core(.NET 10) 백엔드입니다.

> 로그인 브랜치(`VirnectLoginPoc`)는 **참고만** 했고 코드를 가져오거나 수정하지 않았습니다.
> 로그인한 사용자 = 모니터링 열람 관리자로 보고, **모니터링 앱 자체에는 별도 인증이 없습니다**(진입 차단은 로그인 씬이 담당).

## 구조 (두 소비자, 역할 분리)

```
windows_exporter ×4 ─5s→ Prometheus :9090
                              │  CollectorService가 5초마다 /api/v1/query
                              ▼
   ┌──────────── VirnectMonitor (C# ASP.NET Core :47892) ────────────┐
   │  분류(클린/보통/위험)                                            │
   │   ├─→ MonitorStore (메모리): 현재 상태 1벌 + 직전 레벨           │
   │   └─→ SQLite: 이상징후/알림 "이벤트" 기록                        │
   │  GET API ──────────────────────────────────────────────────────│
   └─────────┬───────────────────────────────┬──────────────────────┘
   현재값/심각도(단일 value)            현재상태 + 시계열(Prometheus) + 이벤트
             ▼                                 ▼
      Make&View (AR)                    웹 대시보드 (브라우저)
   고정 URL GET, value 읽음          현재 카드 + 추이 그래프 + 알림 로그
```

- **현재값** = MonitorStore(메모리). **시계열 그래프** = Prometheus range query. **사건 기록** = SQLite.

## 수집 지표 / 임계치

| id | 이름 | 단위 | 보통(warn) | 위험(danger) |
|----|------|------|-----------|-------------|
| cpu | CPU 사용률 | % | 70 | 90 |
| memory | 메모리 사용률 | % | 70 | 90 |
| disk | 디스크 사용률(C:) | % | 80 | 90 |
| net_recv | 네트워크 수신 | B/s | 50MB/s | 100MB/s |
| net_sent | 네트워크 송신 | B/s | 50MB/s | 100MB/s |

> 분류: `value >= danger → 위험`, `value >= warn → 보통`, 그 외 `클린`.
> 임계치/쿼리는 `VirnectMonitor/Models/MetricSpec.cs`에서 조정. (메모리/디스크/네트워크 쿼리는 서버당 값 1개가 되도록 `by(server)` 집계를 추가했습니다.)

## 실행

```powershell
# 방법 1: 배치 파일
run-moni-back.cmd

# 방법 2: 직접
dotnet run --project VirnectMonitor -c Release
```

- 대시보드: http://127.0.0.1:47892/
- 헬스체크: http://127.0.0.1:47892/api/health
- 포트 변경: `run-moni-back.cmd`의 `MONI_PORT` 또는 `ASPNETCORE_URLS`.

## API

### 웹 대시보드용
| 메서드 | 경로 | 설명 |
|--------|------|------|
| GET | `/api/status` | 모든 서버 현재 상태 스냅샷 |
| GET | `/api/server/{server}` | 특정 서버 현재 상태 |
| GET | `/api/metrics` | 지표 정의/임계치 |
| GET | `/api/history/{server}/{metric}?minutes=60&step=15` | **시계열 추이**(Prometheus range query) |
| GET | `/api/anomalies?limit=&server=&metric=` | 이상징후 기록 |
| GET | `/api/alerts?limit=` | 알림 이력 |

### Make&View(AR)용 — 단일 `value` 응답
| 메서드 | 경로 | 응답 `value` | AR 용도 |
|--------|------|------|------|
| GET | `/api/metric/{server}/{metric}` | 실제 수치 (예 53.6) | 숫자 표시 |
| GET | `/api/alert/{server}` | 심각도 0·1·2 | `{값}>=2` → 위험 씬 |
| GET | `/api/alert` | 전체 최악 심각도 | 전체 경보 |

### Make&View 연동 예시
1. 데이터 호출 오브젝트 → `API 직접 호출`, GET, 서버주소 `http://<host>:47892/`, 상대주소 `api/alert/server-01`, 자동·반복 요청, 간격 1초.
2. 데이터 연결 → 서버 응답 필터 `value`.
3. 이벤트 → 트리거 `Receive`, 조건 `{값}>=2`, 액션 `씬 이동`(위험 알림 씬).

## 설정 (`VirnectMonitor/appsettings.json`)

| 키 | 기본값 | 설명 |
|----|--------|------|
| `Monitor:PrometheusUrl` | `http://127.0.0.1:9090` | Prometheus 주소 |
| `Monitor:IntervalSeconds` | `5` | 수집 주기(초) |
| `Monitor:DbPath` | `monitoring.db` | SQLite 파일 |
| `Monitor:GroupLabel` | `server` | 서버 구분 라벨 |

## 산출물 문서
- 계획/체크리스트/결정 근거: `docs/projects/moni-back/`
- 결정·아키텍처 시각화: `docs/resources/mockups/`
