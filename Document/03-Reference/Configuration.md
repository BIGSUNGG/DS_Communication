---
project: DS_Communication
type: reference
status: draft
tags: [reference, configuration]
updated: 2026-09-05
---

# Configuration

런타임·전송 옵션 요약. 상세 API: [[../03-Reference/Public-API|Public-API]].

## MessageQueueOptions (Shared)

| 옵션 | 설명 | 기본(목표) |
| ------ | ------ | ------------ |
| `MaxPendingMessages` | 송신/Handler 큐 백프레셔 상한; 도달 시 **비동기 대기** — 메시지 단위 채널(RUDP) 경로는 슬롯 대기(메시지 보유)까지 동일 상한으로 강제하고 초과 시 **흐름 제어 단절**(`Error`) | `10_000` |
| `InlineDispatch` | `true`면 수신 경로 즉시 디스패치(느린 핸들러가 수신 차단) — **단, 메시지 단위 채널(RUDP) 경로에서는 무시**된다(수신 콜백이 세션 간 공유 폴링 스레드에서 실행되므로 항상 큐 디스패치 강제) | `false` (내부 큐) |
| `CoalesceLimitBytes` | 바이트 채널 송신 시 한 번의 write로 묶는 배치 상한(바이트); 상한 도달 시 즉시 전송 — 배치는 상한 후 최대 1프레임 초과 허용 | `65_536` |
| `FrameTimeout` | 수신 프레임 완료 마감(슬로로리스 방어). 프레임의 **첫 바이트 도착 순간** 시작, 마감 내 미완성 시 `DisconnectReason.Timeout` 단절. 완전 유휴 연결(바이트 0)은 대상 아님 — 하트비트는 앱 책임. `null`/`TimeSpan.Zero`로 비활성화 | `30초` |
| `MaxFrameLength` | 단일 프레임 길이 상한(바이트). 초과 프레임은 송신에서 **격리**(해당 항목만 플러시 예외), 수신에서 **거부**(`Error` 단절) — **메시지 단위 채널(RUDP) 수신에도 동일 적용**(LiteNetLib 재조립 자체 상한 ≒90MB라 역직렬화 전 거부). 절대 상한 `LengthPrefixFramer.MaxFrameLength`(64MB) — 초과 값은 설정 시 거부. 필요 시 앱이 상향 | `4MB` |

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

`RudpTransportOptions` — `RudpListener.Start(options)` · `RudpConnector.ConnectAsync(..., options)`에 전달. 미설정 시 전부 기본값이며 LiteNetLib의 나머지 튜닝 값은 건드리지 않는다.

| 옵션 | 설명 | 기본 |
| ------ | ------ | ------ |
| `MaxConnections` | 동시 수락 접속 수 상한. 도달 시 접속 요청을 **수락 전에 예약한 슬롯** 기준으로 즉시 `Reject()`하고 수락은 계속(거부 접속은 `Accepted` 통지 없음). peer 끊김·채널 Dispose 시 슬롯 회수. `null`이면 무제한. 서버 쪽에서만 의미 | `null` |
| `DisconnectTimeout` | 끊김 판정 시간(ms). UDP는 스트림 끝이 없어 이 값이 **half-open 감지의 유일한 신호**다. 0 이하 거부 | `5000` |
| `ConnectTimeout` | 클라이언트 연결 시도 상한(ms). 침묵 호스트(블랙홀)에 대한 연결 실패를 이 시간 이내에 확정 — LiteNetLib 기본은 약 5초(500ms × 10회) 고정. `null`이면 기본 유지 | `null` |
| `ConnectionKey` | 접속 요청 검증 키. 서버는 이 키와 일치하는 요청만 수락(`AcceptIfKey`), 클라이언트는 이 키로 접속. `null`·빈 문자열 거부 | `"DS_Communication.RUDP"` |
| `IPv6` | IPv6 소켓도 함께 바인딩 | `false` |

- **poll 간격은 옵션이 아니다** — 호스트당 전용 폴링 스레드 1개가 고정 1ms 간격으로 `PollEvents()`를 드레인한다. 스레드 수는 접속 수와 무관하게 고정 — [[../05-Decisions/0007-rudp-three-way-split-and-polling|ADR 0007]].
- `UnsyncedEvents`는 노출하지 않는다(기본 `false` 유지) — `true`면 수신 콜백이 소켓 스레드에서 실행되어 앱 코드가 한 번만 블럭해도 전체 접속의 수신이 멈춘다.
- LiteNetLib 자체 keep-alive(`DisconnectTimeout`)와 앱 하트비트(ping 메시지)는 별개 — 앱이 조율. 라이브러리 하트비트 없음 ([[0003-connection-lifecycle-options]]).
- 분할 불가 전송 방식의 MTU 초과 송신은 `ArgumentException`이며 **세션이 `Disconnected(Error)`로 끊긴다** — [[../03-Reference/Public-API|Public-API]] § RUDP 런타임 의미.

## 재접속

라이브러리 옵션 없음. 앱이 `Disconnected` 후 Connect + 새 Session. [[../04-Guides/Getting-Started|Getting-Started]] § 재접속.

## 관련

- [[../03-Reference/Public-API|Public-API]]
- [[0003-connection-lifecycle-options]]
- [[../05-Decisions/0007-rudp-three-way-split-and-polling|0007-rudp-three-way-split-and-polling]]
- [[../03-Reference/Packages|Packages]]
