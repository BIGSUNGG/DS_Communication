---
project: DS_Communication
type: reference
status: draft
tags: [reference, api]
updated: 2026-09-01
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

## 런타임 의미 (합의 2026-08-31)

- 끊김·Dispose·파이프라인 미부착 세션의 `SendAsync` / `SendAndFlushAsync`는 **예외로 완료된 Task**를 반환한다 (동기 throw 아님, 무시 아님).
- 큐 백프레셔 상한 도달 시 **공간 날 때까지 비동기 대기**한다 (드롭·예외 아님).
- 핸들러 `Action`이 던진 예외는 **Trace 후 수신 루프 계속** — 세션 끊김으로 격상하지 않는다.
- 송신 직렬화·프레임 검증 실패는 **해당 항목의 플러시만 예외 완료**하고 송신 루프 계속 — 세션 끊김으로 격상하지 않는다 (수신 격리와 대칭).
- `InlineDispatch` 기본 `false` (내부 큐) — [[../03-Reference/Configuration|Configuration]].

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

- [[../04-Guides/Getting-Started|Getting-Started]]
- [[Session]] · [[Handler]] · [[Pipeline]]
- [[Implementation-Roadmap]]
