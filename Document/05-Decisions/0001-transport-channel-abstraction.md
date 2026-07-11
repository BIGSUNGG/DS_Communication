---
project: DS_Communication
type: adr
status: draft
tags: [adr, architecture, transport, ipc]
updated: 2026-07-11
---

# ADR 0001: Transport channel abstraction

## Status

Accepted

## Context

레거시 스택은 TCP / TCP_IOCP / RUDP마다 Session·Sender·Receiver를 복제했다. Legacy ADR은 “네 번째 스택이 생기면 `ITransport*`를 도입”이라고 했다. 재작성은 IPC(스트림 Named Pipe·UDS, 이후 Shared Memory)를 같은 구조에 넣어야 하므로, 처음부터 채널 추상화를 Shared에 둔다.

요구:

- 연결형 통신 (Connect/Accept → Session → Disconnect)
- TCP와 스트림 IPC는 동일 바이트+Framing 파이프라인
- RUDP는 메시지 단위
- Shared Memory는 스트림과 다른 produce/consume 모델
- 전송당 Client/Server/Shared 3분할 NuGet은 유지보수 비용이 큼

## Decision

1. **`Communication.Shared`에 채널 계약**을 둔다.
   - `IByteChannel` — TCP, TCP_IOCP, IPC.Stream
   - `IMessageChannel` + `SendOptions` — RUDP
   - `ISharedMemoryChannel` — 후속 Shared Memory (인터페이스 예약)
2. **메시지 파이프라인은 Shared**에 한 번만 둔다. 스트림 전송은 `LengthPrefixFramer`를 공유한다.
3. **스택당 NuGet 1개**: 지금 `Network.TCP`, `Network.TCP_IOCP`, `Network.RUDP`; 후속 `IPC.Stream`, `IPC.SharedMemory`.
4. **앱 공개면은 `ISession` / Connector / Listener**. Channel은 전송 패키지 내부·고급 확장용.
5. **직렬화는 Shared에 구현하지 않는다.** `IMessageConverter`는 `IBufferWriter` Serialize + `ReadOnlySpan` Deserialize ([[0006-session-ownership-and-converter]]).
6. **Session은 앱이 Channel 위에 생성**한다. Connector/Listener는 연결만 담당.

## Consequences

### Positive

- TCP·TCP_IOCP·IPC.Stream이 Framing·큐·coalesce를 공유한다.
- RUDP·SharedMemory는 잘못된 바이트 스트림 모델에 억지로 끼우지 않는다.
- 패키지 수가 줄고, 수정이 Shared 한곳에 모인다.

### Negative

- Shared 계약 설계 실수가 초기에 필요하다.
- RUDP와 스트림의 Send context/`SendOptions` 의미가 달라 문서화가 필수다.

### Neutral

- Legacy 네임스페이스·3분할 패키지와의 바이너리 호환은 목표에 두지 않는다 (`Legacy/` 아카이브).

## Alternatives considered

- 레거시처럼 스택별 Sender/Receiver 복제 — IPC에서 네 번째·다섯 번째 복제가 되므로 거부.
- 모든 전송을 `IByteChannel`만으로 통일 — RUDP·SharedMemory에 부적합하여 거부.
- SharedMemory를 처음부터 완전 명세 — 세부 프로토콜 미정이라 인터페이스 예약만.

## 관련

- [[Overview]]
- [[Data-Flow]]
- [[Code-Structure]]
- [[0002-tcp-backend-selection]]
- [[Packages]]
