---
project: DS_Communication
type: reference
status: draft
tags: [reference, configuration]
updated: 2026-07-11
---

# Configuration

런타임·전송 옵션 요약. 상세 API: [[Public-API]].

## MessageQueueOptions (Shared)

| 옵션 | 설명 | 기본(목표) |
|------|------|------------|
| `MaxPendingMessages` | 송신/Handler 큐 백프레셔 | 구현 시 확정 |
| `InlineDispatch` | Handler 즉시 vs 내부 큐 | `true` 권장(핫패스) |

## SocketKeepAliveOptions (TCP / TCP_IOCP)

| 옵션         | 설명                              |
| ---------- | ------------------------------- |
| `Enabled`  | SO_KEEPALIVE 등 OS keep-alive 적용 |
| `IdleTime` | 연결 유휴 후 첫 probe까지 (가능한 OS만)     |
| `Interval` | probe 간격 (가능한 OS만)              |

`TcpTransportOptions.KeepAlive` / `TcpIocpTransportOptions.KeepAlive`에 설정.

- half-open 감지 보조용. 앱 하트비트(ping 메시지)와 별개.
- Windows / Linux / Unity Player마다 지원·최소값이 다를 수 있음 — 구현 시 플랫폼별 문서화.

## RUDP (LiteNetLib)

- 연결 키, poll 간격 등은 `RudpTransportOptions` (구현 시).
- LiteNetLib 자체 ping/timeout과 앱 ping은 앱이 조율.

## 재접속

라이브러리 옵션 없음. 앱이 `Disconnected` 후 Connect + 새 Session. [[Getting-Started]] § 재접속.

## 관련

- [[Public-API]]
- [[0003-connection-lifecycle-options]]
- [[Packages]]
