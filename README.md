# virnect-project — 통합 백엔드 (backEnd)

Prometheus(windows_exporter)가 수집한 **서버 4대**(server-01~04)의 지표를 **5초마다** 가져와
**클린 / 보통 / 위험** 3단계로 분류하고, 이상징후를 DB에 기록하며,
**Make&View(AR)**, **관리자 로그인**, **웹 대시보드**를 함께 담당하는 C# / ASP.NET Core(.NET 10) 통합 백엔드입니다.

## 통합 기준

이 브랜치는 실제 외부 IP 동작을 확인한 `moni-back`을 운영 기준으로 삼습니다.

```text
base: moni-back
auth source: login/src/VirnectLoginV2/Auth
graph UI source: moni-back-graph/VirnectMonitor/wwwroot
target branch: backEnd
```

통합 정책:

```text
- 모니터링 수집/Prometheus/Docker/외부 접속 구조는 moni-back 기준 유지
- 로그인/회원가입/세션/감사 로그는 login V2 Auth 모듈 이식
- 그래프 대시보드 UI는 moni-back-graph의 wwwroot 파일 이식
- Monitor DB와 Auth DB는 1차 통합에서 분리 운영
```

## 구조 (두 소비자, 역할 분리)

```
windows_exporter ×4 ─5s→ Prometheus :9090
                              │  CollectorService가 5초마다 /api/v1/query
                              ▼
   ┌──────────── VirnectMonitor (C# ASP.NET Core :47892) ────────────┐
   │  분류(클린/보통/위험)                                            │
   │   ├─→ MonitorStore (메모리): 현재 상태 1벌 + 직전 레벨           │
   │   └─→ SQLite: 이상징후/알림 "이벤트" 기록                        │
   │  Auth + GET API ───────────────────────────────────────────────│
   └─────────┬───────────────────────────────┬──────────────────────┘
   현재값/심각도(단일 value)            현재상태 + 시계열(Prometheus) + 이벤트
             ▼                                 ▼
      Make&View (AR)                    웹 대시보드 (브라우저)
   로그인/고정 URL GET, value 읽음    관리자 로그인 + 현재 카드 + 추이 그래프 + 알림 로그
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
dotnet run --project VirnectMonitor -c Release --urls http://127.0.0.1:47892
```

- 로그인/회원가입: http://127.0.0.1:47892/
- 대시보드: http://127.0.0.1:47892/monitoring
- 서버 상세: http://127.0.0.1:47892/monitoring/server?server=server-01
- 헬스체크: http://127.0.0.1:47892/api/health
- 포트 변경: `ASPNETCORE_URLS`.

> `/`는 관리자 계정이 없으면 최초 회원가입으로, 계정이 있으면 로그인으로 진입합니다. 그래프 대시보드는 활성 관리자 세션이 있을 때 `/monitoring`에서 열립니다.

## API

### 관리자 인증용
| 메서드 | 경로 | 설명 |
|--------|------|------|
| GET | `/` | 로그인/회원가입 진입 |
| GET | `/setup` | 최초 관리자 회원가입 화면 |
| POST | `/setup` | 최초 관리자 저장 |
| GET | `/login` | 관리자 로그인 화면 |
| POST | `/auth/login` | 관리자 로그인 처리 |
| GET | `/auth/current-once` | Make&View 로그인 유지 확인 |

### 웹 대시보드용
| 메서드 | 경로 | 설명 |
|--------|------|------|
| GET | `/monitoring` | 그래프 대시보드 화면 |
| GET | `/monitoring/server?server=server-01` | 서버 상세 화면 |
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

### Make&View JSON/URL 사용법

기본 요청 형식:

```text
Method: GET
Headers: 없음
Body: 없음
Response type: JSON
```

Base URL은 실행 환경에 맞게 하나를 사용합니다.

```text
로컬 테스트: http://127.0.0.1:47892
외부/터널 테스트: https://<외부-도메인>
운영 예시: https://<운영-도메인>
```

#### 로그인 유지 확인

```text
GET /auth/current-once
```

전체 URL 예시:

```text
https://<외부-도메인>/auth/current-once
```

응답 예시:

```json
{
  "status": "approved",
  "value": 1,
  "approved": true,
  "authenticated": true,
  "authExpiresAt": "2026-06-04T20:35:40+09:00",
  "username": "admin"
}
```

Make&View에서 읽을 값:

```text
필터: value
value = 1  로그인 유지 중
value = 0  로그인 없음 / 만료 / 해제됨
```

#### 장비현황

전체 장비현황:

```text
GET /api/status
```

서버 1대 장비현황:

```text
GET /api/server/server-01
GET /api/server/server-02
GET /api/server/server-03
GET /api/server/server-04
```

서버 1대 응답 예시:

```json
{
  "server": "server-01",
  "metrics": {
    "cpu": {
      "value": 53.6,
      "display": "53.6%",
      "levelCode": 0,
      "levelText": "클린"
    },
    "memory": {
      "value": 61.2,
      "display": "61.2%",
      "levelCode": 0,
      "levelText": "클린"
    },
    "disk": {
      "value": 72.4,
      "display": "72.4%",
      "levelCode": 1,
      "levelText": "보통"
    }
  },
  "overallCode": 1,
  "overallText": "보통"
}
```

Make&View에서 장비 전체 상태만 조건으로 쓰려면 `overallCode`를 읽습니다.

```text
overallCode = 0  클린
overallCode = 1  보통
overallCode = 2  위험
```

#### 위험도만 간단히 받기

전체 장비 중 최악 위험도:

```text
GET /api/alert
필터: value
```

서버 1대 위험도:

```text
GET /api/alert/server-01
필터: value
```

응답 예시:

```json
{
  "server": "server-01",
  "value": 2,
  "level": "danger",
  "levelText": "위험"
}
```

Make&View 조건 예시:

```text
{값} >= 2  위험 씬 이동
{값} == 1  보통/주의 표시
{값} == 0  정상 표시
```

#### 특정 지표 1개 받기

`/api/metric/server-01`처럼 서버만 넣는 주소는 사용하지 않습니다. 지표 ID까지 넣어야 합니다.

```text
GET /api/metric/server-01/cpu
GET /api/metric/server-01/memory
GET /api/metric/server-01/disk
GET /api/metric/server-01/net_recv
GET /api/metric/server-01/net_sent
```

응답 예시:

```json
{
  "server": "server-01",
  "metric": "cpu",
  "name": "CPU 사용률",
  "value": 53.6,
  "display": "53.6%",
  "level": "clean",
  "levelCode": 0,
  "levelText": "클린",
  "unit": "%"
}
```

Make&View에서 읽을 값:

```text
수치 표시: value
문자 표시: display
위험도 조건: levelCode
```

> Prometheus가 연결되지 않았거나 아직 수집 데이터가 없으면 `/api/status`의 `servers`가 비어 있고, `/api/server/server-01` 또는 `/api/metric/server-01/cpu`가 404를 반환할 수 있습니다. 이 경우 먼저 `/api/health`에서 `prometheusUrl`과 `lastError`를 확인합니다.

## 설정 (`VirnectMonitor/appsettings.json`)

| 키 | 기본값 | 설명 |
|----|--------|------|
| `Monitor:PrometheusUrl` | `http://127.0.0.1:9090` | Prometheus 주소 |
| `Monitor:IntervalSeconds` | `5` | 수집 주기(초) |
| `Monitor:DbPath` | `monitoring.db` | SQLite 파일 |
| `Monitor:GroupLabel` | `server` | 서버 구분 라벨 |
| `Auth:PublicBaseUrl` | 빈 값 | 비워두면 요청 Host 기준으로 로그인 URL 생성 |
| `Auth:DatabasePath` | `Data/auth.db` | 관리자/세션/감사 로그 SQLite 파일 |
| `Auth:ServerSecret` | `development-only-change-me` | 토큰 해시용 비밀값, 운영에서는 환경변수로 변경 |
| `Auth:AuthDurationMinutes` | `5` | 로그인 승인 유지 시간 |

## 산출물 문서
- 계획/체크리스트/결정 근거: `docs/projects/moni-back/`
- 결정·아키텍처 시각화: `docs/resources/mockups/`
