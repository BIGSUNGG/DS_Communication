---
project: DS_Communication
type: adr
status: draft
tags: [adr, architecture, tcp, iocp]
updated: 2026-07-11
---

# ADR 0002: TCP and TCP_IOCP as separate stacks

## Status

Accepted (supersedes earlier draft that merged Stream/IOCP into one TCP package)

## Context

TCP(`NetworkStream`)와 TCP_IOCP(`SocketAsyncEventArgs`)는 프레이밍·세션 계약은 같지만 I/O·튜닝·플랫폼 특성이 다르다. 한 패키지에 Backend 옵션으로 합치면 의존·옵션·샘플이 섞이고, 소비자가 “TCP 하나”로만 보게 되어 스택 선택이 흐려진다.

재작성 범위는 **지금 TCP, TCP_IOCP, RUDP 세 스택**을 명시적으로 만든다. 공통 파이프라인은 Shared의 `IByteChannel`+Framing으로 중복을 줄인다.

## Decision

1. **별도 패키지**로 둔다: `Communication.Network.TCP`, `Communication.Network.TCP_IOCP`, `Communication.Network.RUDP`.
2. TCP와 TCP_IOCP는 각각 **자체 Connector / Listener / Session**을 가진다.
3. 둘 다 **`IByteChannel` + Shared `LengthPrefixFramer` + `MessagePipeline`**을 사용한다. 바이트 채널 구현만 패키지 내부에 둔다.
4. 소비자는 참조할 NuGet으로 스택을 고른다. 런타임 Backend 스위치는 없다.
5. Client/Server/Shared **3분할은 하지 않는다** — 스택(전송)당 NuGet 1개.

## Consequences

### Positive

- 스택 경계가 패키지·네임스페이스로 분명하다.
- IOCP 전용 옵션·샘플이 TCP Stream과 섞이지 않는다.
- Shared 파이프라인으로 레거시식 Sender/Receiver 삼중 복제는 피한다.

### Negative

- TCP·TCP_IOCP에 Session/Channel 래퍼 코드가 두 벌 생긴다 (얇게 유지해야 함).
- 문서·샌드박스도 스택별로 나뉜다.

### Neutral

- IPC.Stream은 이후 `IByteChannel` 스택으로 같은 패턴을 따른다.

## Alternatives considered

- TCP 단일 패키지 + `Backend` 옵션 — 거부 (현재 제품 범위가 세 스택 명시).
- 레거시 Client/Server/Shared 3분할 유지 — 거부 (패키지 폭증).
- IOCP만 제공 — 거부 (이식·단순 경로용 TCP Stream 필요).

## 관련

- [[0001-transport-channel-abstraction]]
- [[Components]]
- [[Code-Structure]]
- [[Packages]]
