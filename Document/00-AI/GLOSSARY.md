---
project: DS_Communication
type: context
status: draft
tags: [ai, glossary]
updated: 2026-09-05
---

# Glossary

| 용어 | 정의 |
| ------ | ------ |
| Session | 앱이 Channel 위에 `new`. 끊김은 Session 이벤트만. 상세: [[Session]] |
| Connector | `ConnectAsync` → `bool`, 성공 시 `Channel`. Session 비생성. |
| Listener | Accept 시 Channel 콜백. Session은 앱. |
| Message Pipeline | Serialize(writer) → Framing → Channel / 역방향 span → Handler. [[Pipeline]] |
| Framing | 4B LE length-prefix (스트림). |
| `IByteChannel` / `IMessageChannel` | I/O. 상세: [[Channel]] |
| `SendOptions` | Shared의 빈 마커 클래스 — 스택별 파생. 기본 송신은 할당 없음. |
| `RudpSendOptions` | `SendOptions` 파생(불변). `RudpDeliveryMethod` 하나를 담고, 전송 방식별 공용 인스턴스 5개를 제공해 송신 할당 0. 기본 `ReliableOrdered`. |
| `RudpDeliveryMethod` | RUDP 패킷 전송 방식 5값 — `ReliableUnordered`=0, `Sequenced`=1, `ReliableOrdered`=2, `ReliableSequenced`=3, `Unreliable`=4. LiteNetLib `DeliveryMethod`와 같은 이름·값이지만 공개면은 이 enum이다(내부 매핑). |
| `RudpTransportOptions` | RUDP 전송 옵션 — `MaxConnections`·`DisconnectTimeout`·`ConnectionKey`·`IPv6`. |
| RUDP 폴링 스레드 | 호스트(리스너/커넥터)당 **1개**의 전용 스레드. `PollEvents()`를 1ms 간격으로 드레인하며 접속 수와 무관하게 고정. 앱 핸들러는 세션별 디스패치 큐에서 돈다 — [[0007-rudp-three-way-split-and-polling]]. |
| Converter | `Serialize(object, IBufferWriter<byte>)` / `Deserialize(ReadOnlySpan<byte>)`. |
| Handler | `void HandleMessage`만. [[Handler]] |
| Coalesce / SendAndFlush | 배치 Write / wire await. |
| Reconnect | **앱 책임** — `ConnectAsync` + `new Session` + (서버) 토큰/핸드셰이크. |
| DisconnectReason | `Local` \| `Remote` \| `Error` \| `Timeout` — `Disconnected` 이벤트 인자. |
| SocketKeepAliveOptions | TCP/TCP_IOCP 전송 옵션 — OS keep-alive (half-open 보조). |
| Disconnect detection | 스트림: EOF·오류. RUDP: peer 끊김 통지(수신 루프가 없어 채널 통지를 세션이 이어 받음)·`DisconnectTimeout`. → `Disconnected(Reason)`. |
| Heartbeat | 앱 책임. |

## 관련

- [[../00-AI/CONTEXT|CONTEXT]] · [[../03-Reference/Public-API|Public-API]] · [[0006-session-ownership-and-converter]] · [[0007-rudp-three-way-split-and-polling]]
