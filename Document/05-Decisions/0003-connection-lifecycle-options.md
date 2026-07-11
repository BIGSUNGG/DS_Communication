---
project: DS_Communication
type: adr
status: draft
tags: [adr, architecture, lifecycle, disconnect]
updated: 2026-07-11
---

# ADR 0003: Disconnect detection and TCP keep-alive options

## Status

Accepted (하트비트·재접속은 앱 책임; 라이브러리는 끊김 통지 + keep-alive 설정만)

## Context

- 하트비트는 앱 프로토콜 영역.
- 재접속은 서버가 항상 새 Session을 받고, 논리 유저/토큰 매핑은 앱(또는 DS_RPC) 책임 → 라이브러리 Channel 재바인딩은 이득 대비 복잡도가 큼.
- half-open 감지는 OS TCP keep-alive 또는 앱 ping으로 보완 가능.

## Decision

1. **끊김 감지 (라이브러리)**
   - channel EOF·전송 예외·명시 `Disconnect` → `MarkDisconnected`
   - Session **`Disconnected(DisconnectReason)`** 이벤트 — Handler 끊김 콜백 없음
2. **`DisconnectReason` (enum)**
   - `Local` — 앱/Session `Disconnect()` 호출
   - `Remote` — 상대 종료·정상 FIN 등
   - `Error` — 예외·프레이밍 오류 등 (선택: `Exception?` 인자)
3. **하트비트 — 앱 책임** (일반 메시지 + 앱 타이머)
4. **재접속 — 앱 책임**
   - `Disconnected` 구독 → 백오프 → `ConnectAsync` → `new *Session(...)` → (서버) 핸드셰이크/토큰
   - `ReconnectOptions`, `Reconnecting`/`Reconnected`/`ReconnectFailed` 이벤트, Channel 재바인딩 **제공하지 않음**
5. **TCP keep-alive (사용자 설정)**
   - TCP / TCP_IOCP Connector·Listener 옵션에 **소켓 keep-alive** 설정 노출
   - 예: `SocketKeepAliveOptions` — `Enabled`, `IdleTime`, `Interval` (OS/플랫폼 한도 내)
   - 기본값은 OS 기본(또는 Off) — 앱이 명시적으로 켜고 간격 설정
   - RUDP는 LiteNetLib/자체 구현 옵션으로 별도 (전송 패키지 문서)

## Consequences

### Positive

- Scope가 명확: 전송 = 연결·송수신·끊김 이유.
- 재접속 큐/flush 경합 구현 불필요.
- 서버·클라이언트 대칭: 둘 다 끊기면 새 Session.

### Negative

- 재접속 backoff·상태 머신은 앱(또는 공통 헬퍼)마다 반복 가능 — Sandbox에 샘플 권장.

### Neutral

- keep-alive만으로는 앱 수준 “살아 있음”과 다름. 앱 ping은 여전히 선택.

## Alternatives considered

- 라이브러리 재접속 + Channel 재바인딩 — 거부 (서버 비대칭·복잡도).
- 라이브러리 하트비트 — 거부.

## 관련

- [[Public-API]]
- [[Getting-Started]]
- [[0004-send-options-and-handler-api]]
- [[0006-session-ownership-and-converter]]
