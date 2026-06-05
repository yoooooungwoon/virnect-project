# VIRNECT Make&View 고객 맞춤형 모니터링 및 이상감지 프로젝트

VIRNECT Make&View를 AR 프론트엔드로 활용해 서버, 컴퓨터, 네트워크 장비 등 인터넷을 사용할 수 있는 장비의 상태를 실시간으로 확인하고 이상징후를 감지하는 고객 맞춤형 모니터링 시스템입니다.

초기 기술계획서는 Linux C/C++ 수집 모듈과 WebSocket 서버를 중심으로 작성했지만, 보안, 유지보수, 통합 편의성을 고려해 현재 구현 방향은 C# ASP.NET Core 백엔드를 기준으로 정리하고 있습니다.

## 프로젝트 컨셉

이 프로젝트는 특정 서버실 하나만을 위한 고정형 모니터링 도구가 아니라, 고객이 관리하는 장비와 환경에 맞게 지표, 임계치, 화면 구성, 알림 방식을 조정할 수 있는 커스터마이징형 모니터링 및 이상감지 시스템을 목표로 합니다.

```text
주제
  서버, 컴퓨터, 기타 인터넷 연결 장비의 실시간 모니터링 및 이상감지 시스템

대상
  서버, 컴퓨터, 네트워크 장비 등 인터넷과 연결된 장비의 관리자

핵심 가치
  고객 환경에 맞는 장비/지표/임계치/대시보드 구성을 제공
```

## 전체 목표

```text
로그인
-> 모니터링 진입
-> 서버 상태 AR 표시
-> 이상감지 상태 표시
-> 버튼 클릭 시 백엔드가 제공하는 웹 그래프 대시보드 열기
```

최종 사용자 흐름은 다음과 같습니다.

```text
VIRNECT View
  -> QR 또는 URL 기반 로그인 화면
  -> 인증 성공
  -> Make&View 모니터링 씬 이동
  -> 서버별 CPU/메모리/디스크/네트워크 상태 확인
  -> 위험 상태면 value 기반 색상/텍스트/씬 제어
  -> 버튼 클릭 시 브라우저 대시보드에서 그래프 확인
```

Discord Webhook 알림은 핵심 로그인-모니터링-그래프 연결이 완료된 뒤 추가할 후속 기능입니다.

## 브랜치 역할

현재 기능은 브랜치별로 나누어 검증 중입니다.

| 브랜치 | 역할 | 상태 |
|---|---|---|
| `main` | 전체 프로젝트 개요 및 통합 방향 정리 | 종합 README |
| `login` | Make&View 진입 전 로그인 게이트 POC | v1 백업 및 v2 인증 설계 정리 |
| `moni-back` | 서버 모니터링/이상감지 백엔드 | C# ASP.NET Core 구현 진행 |

### login 브랜치

Make&View의 제약을 고려해 GET API와 `value` 응답 중심으로 로그인 성공 여부를 전달합니다.

현재 POC는 메모리 기반 code 세션을 사용합니다.

```text
URL 열기
-> 로그인 code 생성
-> ID/PW 로그인
-> Make&View가 /auth/current-once 반복 GET
-> 성공 시 value: 1을 한 번만 반환
-> 이후 같은 성공 신호는 value: 0
```

v2 설계 방향:

```text
토큰은 의미 없는 랜덤 문자열로 유지한다.
DB에는 token_hash만 저장한다.
current-once는 Make&View 통과 신호만 한 번 소비한다.
실제 인증 유지는 auth_expires_at으로 5분간 관리한다.
JWT보다 DB 기반 opaque token 방식을 우선한다.
```

### moni-back 브랜치

모니터링 백엔드는 C# ASP.NET Core로 구성되어 있습니다.

현재 방향:

```text
Prometheus/windows_exporter
-> C# CollectorService
-> clean/warning/danger 분류
-> MonitorStore에 현재 상태 저장
-> SQLite에 anomalies/alerts 이벤트 기록
-> Make&View용 단일 value API 제공
-> 웹 대시보드 정적 파일 및 그래프 API 제공
```

주요 API 예:

```text
GET /api/status
GET /api/server/{server}
GET /api/metric/{server}/{metric}
GET /api/alert/{server}
GET /api/alert
GET /api/history/{server}/{metric}
GET /api/history/{metric}
GET /api/anomalies
GET /api/alerts
```

Make&View에서는 `GET /api/alert/{server}`의 `value`를 조건식에 연결해 위험 상태를 판단할 수 있습니다.

```text
clean   -> value 0
warning -> value 1
danger  -> value 2
```

예시 조건:

```text
{값}>=2
```

## 권장 최종 구조

최종적으로는 로그인 백엔드와 모니터링 백엔드를 따로 운영하기보다, 하나의 C# ASP.NET Core 통합 백엔드 안에서 모듈로 분리하는 구조가 가장 단순합니다.

```text
VirnectMonitor 통합 백엔드
  /auth/*
    - 로그인 토큰 발급
    - 로그인 검증
    - current-once
    - DB + token_hash

  /api/*
    - 서버 현재 상태
    - Make&View용 value API
    - 이상감지 상태
    - 시계열 그래프 API

  /dashboard/*
    - 전체 대시보드
    - 서버별 그래프 화면

  SQLite
    - auth_sessions
    - anomalies
    - alerts
```

이 방식이 적합한 이유:

```text
- 로그인도 DB가 필요하고 모니터링도 DB를 사용한다.
- 인증 상태와 대시보드 접근 제어를 같은 백엔드에서 판단할 수 있다.
- Make&View에 노출할 URL과 API 구조가 단순해진다.
- 배포 포인트가 줄어 시연 안정성이 높아진다.
- 추후 Discord Webhook, HTTPS, 관리자 기능을 붙이기 쉽다.
```

## 인증 설계 요약

Make&View는 POST나 헤더 기반 인증 활용이 제한적이므로, 인증도 GET API와 JSON `value` 응답을 중심으로 설계합니다.

JWT처럼 토큰 안에 상태를 넣는 방식보다, DB 기반 opaque token 방식이 현재 프로젝트에는 더 적합합니다.

```text
사용자/URL에 전달되는 token
  - 랜덤 문자열
  - 내부 의미 없음
  - 짧은 시간만 유효

DB에 저장되는 값
  - token_hash
  - status
  - transition_consumed
  - created_at
  - login_expires_at
  - approved_at
  - auth_expires_at
  - consumed_at
```

중요한 분리:

```text
current-once 소비
  - Make&View에 value: 1 통과 신호를 이미 줬는가

인증 유지
  - 로그인 성공 후 5분 동안 모니터링/대시보드 접근을 허용할 것인가
```

따라서 `consumed`는 인증 만료가 아니라, Make&View 씬 이동 신호가 이미 소비되었다는 뜻으로 다룹니다.

## 모니터링 설계 요약

현재 모니터링 백엔드는 Prometheus에서 서버별 지표를 가져와 분류합니다.

기본 지표:

```text
CPU 사용률
메모리 사용률
디스크 사용률
네트워크 수신
네트워크 송신
```

분류:

```text
clean   - 정상
warning - 보통/주의
danger  - 위험
```

데이터 사용처:

```text
Make&View
  - 단일 value 응답을 읽어 AR 텍스트, 색상, 씬 이동에 사용

웹 대시보드
  - 현재 상태 카드
  - 서버별 상세 화면
  - Prometheus range query 기반 그래프
  - 이상징후/알림 이력
```

## Make&View 연동 기준

로그인 성공 확인:

```text
GET /auth/current-once
응답 필터: value
조건: {값}>0
```

서버 위험 감지:

```text
GET /api/alert/server-01
응답 필터: value
조건: {값}>=2
```

서버 지표 표시:

```text
GET /api/metric/server-01/cpu
응답 필터: value
```

그래프 대시보드 이동:

```text
URL 열기
http://<통합백엔드>:47892/
http://<통합백엔드>:47892/server.html?server=server-01
```

## 현재 우선순위

1. `moni-back`을 기준으로 C# 통합 백엔드 구조 확정
2. `login`의 인증 흐름을 소스 형태로 재구현
3. `auth_sessions` DB 테이블 추가
4. `token_hash`와 `auth_expires_at` 기반 인증 유지 구현
5. `/auth/current-once`의 1회 통과 신호를 DB transaction으로 처리
6. Make&View 씬에서 로그인 성공 후 모니터링 씬으로 연결
7. 대시보드 버튼 URL 연결
8. 최종 통합 시연 흐름 검증

후속 작업:

```text
- Discord Webhook 알림
- HTTPS 적용
- 실제 사용자 계정 저장소
- 관리자용 세션 조회/폐기 화면
- 다중 사용자/다중 장비 세션 분리
```

## 문서 위치

브랜치별 세부 문서는 각 브랜치 README를 기준으로 관리합니다.

```text
main README.md
  - 전체 프로젝트 개요와 통합 방향

login README.md
  - 로그인 게이트 v2 설계

login README.v1.md
  - 기존 로그인 POC 문서 백업

moni-back README.md
  - 모니터링 백엔드 실행/구조/API 문서
```
