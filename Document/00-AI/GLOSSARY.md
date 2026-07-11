---
project: DS_Communication
type: context
status: draft
tags: [ai, glossary]
updated: 2026-07-11
---

# Glossary

| 용어 | 정의 |
|------|------|
| Session | 앱이 Channel 위에 `new`. 끊김은 Session 이벤트만. 상세: [[Session]] |
| Connector | `ConnectAsync` → `bool`, 성공 시 `Channel`. Session 비생성. |
| Listener | Accept 시 Channel 콜백. Session은 앱. |
| Message Pipeline | Serialize(writer) → Framing → Channel / 역방향 span → Handler. [[Pipeline]] |
| Framing | 4B LE length-prefix (스트림). |
| `IByteChannel` / `IMessageChannel` | I/O. 상세: [[Channel]] |
| `SendOptions` / `RudpSendOptions` | 송신 옵션. 스택별 파생. |
| Converter | `Serialize(object, IBufferWriter<byte>)` / `Deserialize(ReadOnlySpan<byte>)`. |
| Handler | `void HandleMessage`만. [[Handler]] |
| Coalesce / SendAndFlush | 배치 Write / wire await. |
| Reconnect | **앱 책임** — `ConnectAsync` + `new Session` + (서버) 토큰/핸드셰이크. |
| DisconnectReason | `Local` \| `Remote` \| `Error` — `Disconnected` 이벤트 인자. |
| SocketKeepAliveOptions | TCP/TCP_IOCP 전송 옵션 — OS keep-alive (half-open 보조). |
| Disconnect detection | EOF·오류·`Disconnect` → `Disconnected(Reason)`. |
| Heartbeat | 앱 책임. |

## 관련

- [[CONTEXT]] · [[Public-API]] · [[0006-session-ownership-and-converter]]
