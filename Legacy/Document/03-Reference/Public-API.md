---
project: DS_Communication
type: reference
status: draft
tags: [api]
updated: 2026-07-11
---

# Public API

공개 타입·진입점 레퍼런스.

**네임스페이스**

| 권장 (canonical) | 호환 (legacy) |
|------------------|---------------|
| `Communication.Shared.*` | — |
| `Communication.Network.TCP.Shared.*` | `Communication.TCP.Shared.*` |
| `Communication.Network.TCP.{Client,Server}` | — |
| `Communication.Network.TCP_IOCP.*` | — |
| `Communication.Network.RUDP.*` | — |

TCP Session/Sender/Receiver는 legacy `Communication.TCP.Shared.*`에 구현이 있고, canonical 네임스페이스에 별칭 타입이 있다. 신규 코드는 `Communication.Network.*`를 선호한다 ([[0001-transport-pipeline-unification]]).

## 연결·수락

| API | 어셈블리 | 설명 |
|-----|----------|------|
| `TCPConnector(host, port)` / `ConnectAsync(Func<TcpClient, Task>, ct)` | TCP.Client | TcpClient 연결 후 콜백 |
| `TCPListener(IPAddress, port)` / `Start`·`Stop`·`ListenAsync` | TCP.Server | Accept 루프 |
| `TCPConnector` / `ConnectAsync(Func<Socket, Task>, ct)` | TCP_IOCP.Client | SocketAsyncEventArgs Connect |
| `TCPListener` | TCP_IOCP.Server | Socket Accept 기반 리스너 |
| `RUDPConnector(host, port, connectionKey="", pollIntervalMs=1)` / `ConnectAsync(...)` | RUDP.Client | LiteNetLib 연결 + poll; `PollIntervalMs` |
| `RUDPListener(..., pollIntervalMs=1)` / `Start`·`Stop`·`ListenAsync`·`ReceiveDispatcher` | RUDP.Server | peer 수락; `PollIntervalMs` |

## 세션

| API | 설명 |
|-----|------|
| `ISession` | `SendAsync`, `SendAndFlushAsync`, `Disconnect`, `IsConnected()` |
| `Session` | abstract; Receiver/Sender factory로 구성. `IsConnected()` = **로컬 플래그** (`MarkDisconnected`) **AND** transport. `SendAndFlushAsync`는 Sender로 위임 |
| `TCPSession` (TCP / TCP_IOCP.Shared) | TcpClient 또는 Socket 래핑 |
| `RUDPSession` | NetPeer + NetManager; peer/manager는 외부 소유 |

앱은 `Session` / `TCPSession` / `RUDPSession`을 상속해 Handler·Converter를 묶는다 (Sandbox `ClientSession` 등).

## 메시지·큐

| API | 설명 |
|-----|------|
| `IMessageConverter` | `byte[] Serialize(object)` / `object Deserialize(ReadOnlySpan<byte>)` — Serialize `byte[]` 할당은 Open ([[Known-Issues]]) |
| `IMessageSender` / `MessageSender` | `SendAsync`; `SendAndFlushAsync` — 큐잉 후 wire 기록까지 await |
| `IMessageReceiver` / `MessageReceiver` | 수신 추상 |
| `IMessageHandler` | `HandleMessage`, `OnDetectedDisconnection` |
| `MessageHandler` | abstract; `RegisterMessageType()`, 타입→Action 맵. 미등록 타입은 Trace 후 skip. `MessageQueueOptions`로 백프레셔·`InlineDispatch` |
| `MessageQueueOptions` | `MaxPendingMessages` (기본 10_000), `InlineDispatch` |
| `SignalGate` (`Communication.Shared.Threading`) | 연속 Signal에도 `SemaphoreFullException` 없음; Sender/Handler 웨이크업에 사용 |
| `TCPMessageSender` / `TCPMessageReceiver` | length-prefix; 송신 coalesce·ArrayPool; 수신 ArrayPool |
| `RUDPMessageSender` / `RUDPMessageReceiver` | LiteNetLib Send / Dispatch |
| `RUDPNetworkReceiveDispatcher` | peer → Receiver 단일 구독 분배 |
| `MessageSendContext` / `ReliableType` | RUDP DeliveryMethod 선택 |

`SendAsync`는 fire-and-forget 큐잉이다. wire 완료가 필요하면 `SendAndFlushAsync`를 쓴다.

## 관련

- [[Packages]]
- [[Data-Flow]]
- [[Configuration]]
- [[Getting-Started]]
- [[How-To]]
- [[0001-transport-pipeline-unification]]
