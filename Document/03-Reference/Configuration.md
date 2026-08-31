---
project: DS_Communication
type: reference
status: draft
tags: [reference, configuration]
updated: 2026-09-01
---

# Configuration

런타임·전송 옵션 요약. 상세 API: [[../03-Reference/Public-API|Public-API]].

## MessageQueueOptions (Shared)

| 옵션 | 설명 | 기본(목표) |
| ------ | ------ | ------------ |
| `MaxPendingMessages` | 송신/Handler 큐 백프레셔 상한; 도달 시 **비동기 대기** | `10_000` |
| `InlineDispatch` | `true`면 수신 경로 즉시 디스패치(느린 핸들러가 수신 차단) | `false` (내부 큐) |
| `CoalesceLimitBytes` | 바이트 채널 송신 시 한 번의 write로 묶는 배치 상한(바이트); 상한 도달 시 즉시 전송 — 배치는 상한 후 최대 1프레임 초과 허용 | `65_536` |
| `FrameTimeout` | 수신 프레임 완료 마감(슬로로리스 방어). 프레임의 **첫 바이트 도착 순간** 시작, 마감 내 미완성 시 `DisconnectReason.Timeout` 단절. 완전 유휴 연결(바이트 0)은 대상 아님 — 하트비트는 앱 책임. `null`/`TimeSpan.Zero`로 비활성화 | `30초` |

## NoDelay (TCP)

| 옵션 | 설명 | 기본 |
| ------ | ------ | ------------ |
| `NoDelay` | TCP_NODELAY(Nagle 해제). 라이브러리가 이미 coalesce로 송신을 묶으므로 Nagle은 중복 지연만 추가 | `true` |

`TcpTransportOptions.NoDelay`에 설정 — `TcpConnector`/`TcpListener`가 연결·수락 소켓에 적용. `false`면 OS 설정을 유지한다.

## MaxConnections (TCP)

| 옵션 | 설명 | 기본 |
| ------ | ------ | ------------ |
| `MaxConnections` | 동시 수락 연결 수 상한(연결 고갈 공격 방어). 상한 도달 시 새 수락 연결은 **즉시 닫고 수락 계속** — 거부 연결은 `Accepted` 통지를 받지 않는다. 채널(세션) Dispose 시 슬롯 회수. `0`·음수 거부 | `null`(무제한) |

`TcpTransportOptions.MaxConnections`에 설정 — `TcpListener`가 수락 루프에서 강제하고 `ActiveConnectionCount`로 현황을 노출한다.

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

라이브러리 옵션 없음. 앱이 `Disconnected` 후 Connect + 새 Session. [[../04-Guides/Getting-Started|Getting-Started]] § 재접속.

## 관련

- [[../03-Reference/Public-API|Public-API]]
- [[0003-connection-lifecycle-options]]
- [[../03-Reference/Packages|Packages]]
