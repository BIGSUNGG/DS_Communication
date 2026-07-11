---
project: DS_Communication
type: adr
status: draft
tags: [adr, api, session, converter]
updated: 2026-07-11
---

# ADR 0006: Session ownership and Converter buffers

## Status

Accepted

## Context

1. Session을 Connector가 만들어 반환하면 앱이 Converter/Handler/lifecycle을 주입하기 어색하고, Connect API가 비대해진다.
2. 끊김을 Session 이벤트와 Handler `OnDetectedDisconnection`에 두면 이중 처리·누락이 난다.
3. Legacy `Serialize → byte[]`는 메시지마다 힙 할당을 강제한다 (Known-Issue).

## Decision

1. **Session 생성은 앱 (패턴 C)**  
   - `ConnectAsync` / Accept는 **Channel(또는 동등 핸들)** 만 제공한다.  
   - 앱이 `new TcpSession(channel, converter, handlerFactory)` (및 RUDP/IOCP 대응 타입)를 호출한다.  
   - 라이브러리는 “연결된 전송”과 “메시지 세션”을 분리한다.
2. **끊김 처리는 Session만**  
   - `Disconnected(DisconnectReason)`만. Pipeline은 Session.`MarkDisconnected` 호출.
3. **Converter는 버퍼 계약**  
   - `void Serialize(object message, IBufferWriter<byte> writer)`  
   - `object Deserialize(ReadOnlySpan<byte> message)`  
   - Pipeline은 length-prefix 헤더와 payload를 writer/span 경로로 조합·해체한다.

## Consequences

### Positive

- 앱이 Session 수명·의존 주입을 명시적으로 통제한다.
- 끊김 구독이 한곳이다.
- 고성능 Converter(DS_MessageProtocol)가 풀/writer에 직접 쓸 수 있다.

### Negative

- Accept/Connect마다 `new Session` 보일러플레이트가 앱(또는 앱 헬퍼)에 생긴다.
- `IBufferWriter` 미숙 구현은 여전히 내부 할당 가능 — 계약만으로는 GC 제로 보장 아님.

### Neutral

- 간단 헬퍼 `SessionFactory`를 Sandbox에 둘 수 있으나 Shared 필수는 아님.

## Alternatives considered

- Connect가 Session 반환 — 거부 (주입·옵션 결합도↑).
- Handler에도 끊김 콜백 — 거부 (이중 경로).
- `Serialize → byte[]` 유지 — 거부 (재작성에서 할당 계약 제거).

## 관련

- [[Public-API]]
- [[Getting-Started]]
- [[0004-send-options-and-handler-api]]
- [[0001-transport-channel-abstraction]]
