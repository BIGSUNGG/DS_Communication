---
project: DS_Communication
type: architecture
status: draft
tags: [architecture, components]
updated: 2026-07-11
---

# Components

핵심 타입의 책임 요약. 시그니처는 [[Public-API]].

**핵심 개념 상세:** [[Session]] · [[Pipeline]] · [[Channel]] · [[Handler]]

## Shared — 연결·세션

| 타입 | 책임 |
| ------ | ------ |
| `IConnector` | `ConnectAsync` → `bool`, 성공 시 `Channel` 노출. Session은 만들지 않음. |
| `IListener` | Accept 시 Channel 콜백. Session은 앱이 생성. |
| `ISession` | Send / Disconnect / IsConnected / `Disconnected(DisconnectReason)`. |
| `Session` | 앱이 `new`. Pipeline 소유. |
| `DisconnectReason` | `Local` \| `Remote` \| `Error`. |
| `IMessageConverter` | `Serialize(..., IBufferWriter<byte>)` / `Deserialize(ReadOnlySpan<byte>)`. |
| `IMessageHandler` / `MessageHandler` | `void HandleMessage`만. 끊김 콜백 없음. |
| `SendOptions` | 송신 부가 옵션 기반 타입. |

이벤트: **`Disconnected`만** (재접속 이벤트 없음 — [[0003-connection-lifecycle-options]]).

## Shared — 메시지

| 타입 | 책임 |
|------|------|
| `MessagePipeline` | 큐, coalesce, flush, Converter writer/span, Framer·Channel. |
| `MessageQueueOptions` | MaxPendingMessages, coalesce, InlineDispatch. |

## Shared — 채널·프레이밍

| 타입 | 책임 |
| ------ | ------ |
| `IByteChannel` | Read/Write. TCP·TCP_IOCP·IPC.Stream. |
| `IMessageChannel` | 메시지 Send + 수신. RUDP. |
| `ISharedMemoryChannel` | Claim/Commit·Consume. 후속. |
| `LengthPrefixFramer` | 4B LE length-prefix. |

## Shared — 유틸

| 타입 | 책임 |
|------|------|
| `SignalGate` | 송신 루프 깨우기 등. |

## 소유권

- **앱**이 Session을 `new`하고 Dispose/Disconnect를 통제한다.
- Session → Pipeline → Channel(세션 소유분).
- RUDP NetManager는 Connector/Listener 소유 가능.
- **하트비트·재접속**은 앱.

## Network.TCP

| 타입 | 책임 |
| ------ | ------ |
| `TcpConnector` / `TcpListener` | TCP 연결·수락. 리스너는 `MaxConnections` 상한 강제 — 초과 수락 연결은 즉시 닫고 수락 계속, `ActiveConnectionCount`로 현황 노출. |
| `TcpSession` | `IByteChannel` + Framer + Pipeline. |
| `TcpTransportOptions` | `NoDelay`·`KeepAlive`·**`MaxConnections`**(동시 수락 상한). |
| `SocketKeepAliveOptions` | OS TCP keep-alive (사용자 설정). |
| `StreamByteChannel` | `NetworkStream` → `IByteChannel`. Dispose 훅으로 리스너 연결 수 회수. |

## Network.TCP_IOCP

| 타입 | 책임 |
| ------ | ------ |
| `TcpIocpConnector` / `TcpIocpListener` | Socket 연결·수락. |
| `TcpIocpSession` | 동일 Framer·Pipeline, IOCP 채널. |
| `TcpIocpTransportOptions` | IOCP 버퍼·풀·동시성·**`KeepAlive`**. |
| `IocpByteChannel` | SAEA → `IByteChannel`. |

## Network.RUDP

| 타입 | 책임 |
| ------ | ------ |
| `RudpConnector` / `RudpListener` | LiteNetLib 연결·수락·펌프. |
| `RudpSession` | `IMessageChannel` + Pipeline. |
| `RudpMessageChannel` | peer 매핑. |
| `RudpSendOptions` | `SendOptions` 파생 — 신뢰성/Delivery 힌트. |

## IPC (후속)

| 패키지 | 핵심 |
|--------|------|
| `IPC.Stream` | NamedPipe/UDS → `IByteChannel` |
| `IPC.SharedMemory` | `ISharedMemoryChannel` |

## 관련

- [[Overview]]
- [[Data-Flow]]
- [[Code-Structure]]
- [[Session]]
- [[Pipeline]]
- [[Channel]]
- [[Handler]]
- [[Public-API]]
- [[Configuration]]
- [[0003-connection-lifecycle-options]]
- [[0004-send-options-and-handler-api]]
- [[0006-session-ownership-and-converter]]
- [[Packages]]
