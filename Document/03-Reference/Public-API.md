---
project: DS_Communication
type: reference
status: draft
tags: [reference, api]
updated: 2026-07-11
---

# Public API

합의된 공개 계약. ADR: [[0004-send-options-and-handler-api]], [[0006-session-ownership-and-converter]], [[0003-connection-lifecycle-options]].

## Connect / Listen — Session은 앱이 생성

```text
// Client
Task<bool> ConnectAsync(..., CancellationToken cancellationToken = default);
IByteChannel Channel { get; }   // TCP / TCP_IOCP — Connect 성공 후

// 앱
if (!await connector.ConnectAsync(host, port)) return;
var session = new TcpSession(connector.Channel, converter, s => new ChatHandler(s));

// Server
listener.Accepted += channel => { var session = new TcpSession(channel, ...); };
listener.Start(...);
```

## TCP keep-alive (사용자 설정)

TCP / TCP_IOCP Connector·Listener 생성 시 옵션으로 전달.

```text
class SocketKeepAliveOptions
{
    bool Enabled;
    TimeSpan IdleTime;      // 유휴 후 첫 probe (OS 지원 범위)
    TimeSpan Interval;      // probe 간격
}

class TcpTransportOptions
{
    SocketKeepAliveOptions? KeepAlive;  // null = OS 기본, 명시 시 적용
    // ... 버퍼·타임아웃 등
}
```

- `Enabled = false` 또는 옵션 생략: keep-alive 변경 없음(또는 OS 기본).
- Unity/netstandard2.1에서 일부 필드는 OS가 무시할 수 있음 — 문서/Configuration에 플랫폼 노트.

## ISession

```text
Task SendAsync(object message);
Task SendAsync(object message, SendOptions? options);
Task SendAndFlushAsync(object message, SendOptions? options = null, CancellationToken cancellationToken = default);

void Disconnect();
bool IsConnected();

event EventHandler<DisconnectedEventArgs> Disconnected;
// DisconnectedEventArgs.Reason: DisconnectReason { Local, Remote, Error }
// DisconnectedEventArgs.Exception? (Error일 때)
```

**재접속 이벤트 없음.** 앱이 `Disconnected` 후 `ConnectAsync` + `new Session`.

## SendOptions

```text
class SendOptions { }
class RudpSendOptions : SendOptions { /* ReliableType 등 */ }
```

## Handler

```text
interface IMessageHandler
{
    void HandleMessage(object message);
}
```

## Converter

```text
interface IMessageConverter
{
    void Serialize(object message, IBufferWriter<byte> writer);
    object Deserialize(ReadOnlySpan<byte> message);
}
```

## 관련

- [[Getting-Started]]
- [[Session]] · [[Handler]] · [[Pipeline]]
- [[Implementation-Roadmap]]
