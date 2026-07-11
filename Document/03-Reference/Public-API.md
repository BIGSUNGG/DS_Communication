---
project: DS_Communication
type: reference
status: draft
tags: [api]
updated: 2026-07-11
---

# Public API

공개 타입·진입점 레퍼런스. 네임스페이스는 어셈블리마다 다름 (`Communication.Shared.*`, `Communication.Network.*`, `Communication.TCP.Shared.*` 등).

## 연결·수락

| API | 어셈블리 | 설명 |
|-----|----------|------|
| `TCPConnector(host, port)` / `ConnectAsync(Func<TcpClient, Task>, ct)` | TCP.Client | TcpClient 연결 후 콜백 |
| `TCPListener(IPAddress, port)` / `Start`·`Stop`·`ListenAsync` | TCP.Server | Accept 루프, 클라이언트당 Task.Run |
| `TCPConnector` / `ConnectAsync(Func<Socket, Task>, ct)` | TCP_IOCP.Client | SocketAsyncEventArgs Connect |
| `TCPListener` | TCP_IOCP.Server | Socket Accept 기반 리스너 |
| `RUDPConnector(host, port, connectionKey="")` / `ConnectAsync(Func<NetPeer, NetManager, EventBasedNetListener, Task>, ct)` | RUDP.Client | LiteNetLib 연결 + poll |
| `RUDPListener` / `Start`·`Stop`·`ListenAsync`·`ReceiveDispatcher` | RUDP.Server | peer 수락, Dispatcher 노출 |

## 세션

| API | 설명 |
|-----|------|
| `ISession` | `SendAsync(message[, context])`, `Disconnect()` |
| `Session` | abstract; Receiver/Sender factory로 구성, `IsConnected`, `Dispose` |
| `TCPSession` (TCP / TCP_IOCP.Shared) | TcpClient 또는 Socket 래핑 |
| `RUDPSession` | NetPeer + NetManager; peer/manager는 외부 소유 |

앱은 `Session` / `TCPSession` / `RUDPSession`을 상속해 Handler·Converter를 묶는다 (Sandbox `ClientSession` 등).

## 메시지

| API | 설명 |
|-----|------|
| `IMessageConverter` | `byte[] Serialize(object)` / `object Deserialize(ReadOnlySpan<byte>)` |
| `IMessageSender` / `MessageSender` | 송신 추상 |
| `IMessageReceiver` / `MessageReceiver` | 수신 추상 |
| `IMessageHandler` | `HandleMessage`, `OnDetectedDisconnection` |
| `MessageHandler` | abstract; `RegisterMessageType()`, 타입→Action 맵, 수신 큐 |
| `TCPMessageSender` / `TCPMessageReceiver` | length-prefix 프레이밍 |
| `RUDPMessageSender` / `RUDPMessageReceiver` | LiteNetLib Send / Dispatch |
| `RUDPNetworkReceiveDispatcher` | peer → Receiver 단일 구독 분배 |
| `MessageSendContext` / `ReliableType` | RUDP DeliveryMethod 선택 |

## 관련

- [[Packages]]
- [[Data-Flow]]
- [[Getting-Started]]
- [[How-To]]
