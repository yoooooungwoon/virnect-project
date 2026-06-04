# mk2 배포 가이드

이 설정은 기존 `docker-compose.yml` 배포를 건드리지 않고, `moni-back-graph` 화면을 별도 mk2 환경으로 띄우기 위한 구성입니다.

## 구성

- 기존 배포: `virnect-monitor`, `cloudflared`, `monitor-data`
- mk2 배포: `virnect-monitor-mk2`, `cloudflared-mk2`, `monitor-mk2-data`
- Prometheus: 기존 `prometheus_default` Docker 네트워크를 같이 사용
- Cloudflare hostname 대상: `http://virnect-monitor-mk2:47892`

## 실행

1. Cloudflare Zero Trust에서 mk2용 Tunnel 또는 Public Hostname을 준비합니다.
2. mk2 hostname을 `http://virnect-monitor-mk2:47892`로 연결합니다.
3. `.env.mk2.example`을 `.env.mk2`로 복사하고 `MK2_TUNNEL_TOKEN`을 입력합니다.
4. 서버에서 아래 명령으로 mk2만 실행합니다.

```powershell
docker compose --env-file .env.mk2 -f docker-compose.mk2.yml up -d --build
```

## 확인

```powershell
docker compose --env-file .env.mk2 -f docker-compose.mk2.yml ps
docker compose --env-file .env.mk2 -f docker-compose.mk2.yml logs -f virnect-monitor-mk2
```

mk2 앱도 기존 Prometheus를 바라보기 때문에 `server-01`, `server-02`가 구동 중이면 자동으로 표시됩니다. `server-03`, `server-04`는 나중에 Prometheus에 붙으면 별도 코드 수정 없이 화면에 나타납니다.
