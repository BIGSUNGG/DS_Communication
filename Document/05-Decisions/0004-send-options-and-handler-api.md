---
project: DS_Communication
type: adr
status: draft
tags: [adr, api, send-options, handler]
updated: 2026-08-31
---

# ADR 0004: Connect / Handler / SendOptions API

## Status

Accepted

## Context

공개 API를 스택마다 다르게 두면 앱·테스트 비용이 커진다. Connect 실패 표현, Handler 동기성, Send 부가 옵션(특히 RUDP 신뢰성)을 한 번에 고정한다.

## Decision

### Connect / Session 생성

- `ConnectAsync(...)` → **`bool`**. 성공 후 Connector가 **`Channel`** 을 노출한다.
- **Session은 앱이 생성**한다 (`new TcpSession(channel, ...)`). 상세 [[0006-session-ownership-and-converter]].
- 취소 시 `OperationCanceledException`.

### Handler (속도·구조 우선)

- `HandleMessage(object message)` — **`void` 동기**.
- 타입 등록: `RegisterMessageType` + `Action<object>` / `Register<T>`.
- `InlineDispatch`로 즉시/큐 선택. **단, 메시지 단위 채널(`IMessageChannel`) 경로는 무시하고 항상 큐 강제**(수신 콜백이 세션 간 공유 폴링 스레드 — 느린 핸들러로 인한 타세션 차단 방지, 2026-09-05 후속 수정). `async` Handler API 없음.
- **끊김 콜백 없음** — Session `Disconnected`만 사용.

### Converter

- `Serialize(object, IBufferWriter<byte>)` / `Deserialize(ReadOnlySpan<byte>)` — [[0006-session-ownership-and-converter]].

### SendOptions

- Shared에 기반 타입 **`SendOptions`** — 필드 없는 마커 **클래스**로 확정(2026-08-31). 할당은 파생 옵션(`RudpSendOptions` 등) 사용 시에만.
- `ISession.SendAsync(object message)` / `SendAsync(object message, SendOptions? options)` / `SendAndFlushAsync(..., SendOptions? options, ...)`.
- 스택별 확장: **상속(또는 스택 패키지 파생 타입)**.
  - 예: `RudpSendOptions : SendOptions` — `ReliableType` 등
  - TCP / TCP_IOCP는 부가 필드 없으면 `SendOptions`만 쓰거나 빈 `TcpSendOptions` (필요 시)
- Pipeline은 `SendOptions`를 Channel까지 전달; RUDP Channel이 `RudpSendOptions`로 캐스팅/패턴 매칭.

### Lifecycle

- **`Disconnected(DisconnectReason)`** 만 제공. `Local` | `Remote` | `Error`.
- **재접속 이벤트/옵션 없음** — 앱 책임 ([[0003-connection-lifecycle-options]]).
- 의도적 `Disconnect()` → `Disconnected(Local)`; 재접속 루프는 앱이 Reason으로 판단.

## Consequences

### Positive

- bool Connect로 호출부가 단순하다.
- 동기 Handler가 핫패스·Unity 친화적이다.
- RUDP 옵션이 타입으로 확장 가능하다.

### Negative

- 비동기 수신 처리 규약은 앱에 맡긴다.
- `SendOptions` 참조 타입이면 송신당 할당 가능성 — 필요 시 struct/풀로 최적화.

## 관련

- [[../03-Reference/Public-API|Public-API]]
- [[Session]]
- [[Handler]]
- [[Pipeline]]
- [[0003-connection-lifecycle-options]]
- [[0006-session-ownership-and-converter]]
