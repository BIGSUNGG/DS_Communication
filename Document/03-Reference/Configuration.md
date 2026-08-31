---
project: DS_Communication
type: reference
status: draft
tags: [reference, configuration]
updated: 2026-08-31
---

# Configuration

런타임·전송 옵션 요약. 상세 API: [[../03-Reference/Public-API|Public-API]].

## MessageQueueOptions (Shared)

| 옵션 | 설명 | 기본(목표) |
|------|------|------------|
| `MaxPendingMessages` | 송신/Handler 큐 백프레셔 상한; 도달 시 **비동기 대기** | `10_000` |
| `InlineDispatch` | `true`면 수신 경로 즉시 디스패치(느린 핸들러가 수신 차단) | `false` (내부 큐) |

## SocketKeepAliveOptions (TCP / TCP_IOCP)

| 옵션 | 설명 |
| ---------- | ------------------------------- |
| `Enabled` | SO_KEEPALIVE 등 OS keep-alive 적용 |
| `IdleTime` | 연결 유휴 후 첫 probe까지 (가능한 OS만) |
| `Interval` | probe 간격 (가능한 OS만) |

`TcpTransportOptions.KeepAlive` / `TcpIocpTransportOptions.KeepAlive`에 설정.

- half-open 감지 보조용. 앱 하트비트(ping 메시지)와 별개.
- Windows는 `SIO_KEEPALIVE_VALS` IOControl, Unix는 원시 TCP 옵션(Linux `TCP_KEEPIDLE`=4/`TCP_KEEPINTVL`=5, macOS 유휴 `TCP_KEEPALIVE`=16)으로 적용한다. 미지원 필드는 조용히 무시된다.

## RUDP (LiteNetLib)

- 연결 키, poll 간격 등은 `RudpTransportOptions` (구현 시).
- LiteNetLib 자체 ping/timeout과 앱 ping은 앱이 조율.

## 재접속

라이브러리 옵션 없음. 앱이 `Disconnected` 후 Connect + 새 Session. [[Getting-Started]] § 재접속.

## 관련

- [[../03-Reference/Public-API|Public-API]]
- [[0003-connection-lifecycle-options]]
- [[../03-Reference/Packages|Packages]]
