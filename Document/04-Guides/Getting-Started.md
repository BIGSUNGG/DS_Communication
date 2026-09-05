---
project: DS_Communication
type: guide
status: draft
tags: [guide, usage, examples]
updated: 2026-09-05
---

# Getting Started — 사용 예시

[[../03-Reference/Public-API|Public-API]], [[0006-session-ownership-and-converter]], [[0003-connection-lifecycle-options]].

## 1. Converter · Handler

```csharp
using System.Buffers;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;

public sealed class ChatMessage { public string Text { get; set; } = ""; }

public sealed class DemoConverter : IMessageConverter
{
    public void Serialize(object message, IBufferWriter<byte> writer) { /* ... */ }
    public object Deserialize(ReadOnlySpan<byte> data) => /* ... */;
}

public sealed class ChatHandler : MessageHandler
{
    public ChatHandler(ISession session) : base(session)
    {
        Register<ChatMessage>(m => Console.WriteLine($"recv: {m.Text}"));
    }
}
```

## 2. TCP + keep-alive 옵션

```csharp
using Communication.Network.TCP;

var options = new TcpTransportOptions
{
    KeepAlive = new SocketKeepAliveOptions
    {
        Enabled = true,
        IdleTime = TimeSpan.FromSeconds(30),
        Interval = TimeSpan.FromSeconds(5),
    }
};

var connector = new TcpConnector();
if (!await connector.ConnectAsync("127.0.0.1", 7777, options)) return;

var session = new TcpSession(connector.Channel!, converter, s => new ChatHandler(s));

session.Disconnected += (_, e) =>
    Console.WriteLine($"disconnected: {e.Reason}" + (e.Exception is null ? "" : $" {e.Exception.Message}"));
```

서버 `TcpListener`에도 동일하게 `TcpTransportOptions.KeepAlive` 전달.

## 3. TCP 서버

```csharp
var listener = new TcpListener(IPAddress.Any, 7777); // using System.Net
listener.Accepted += channel =>
{
    var session = new TcpSession(channel, converter, s => new ChatHandler(s));
    session.Disconnected += (_, e) => Console.WriteLine($"peer left: {e.Reason}");
};
listener.Start(options);
```

## 4. 앱 재접속 (라이브러리 기능 아님)

클라이언트·서버 모두 **끊기면 새 Session**. 서버는 토큰으로 같은 유저에 새 Session을 붙인다.

```csharp
async Task RunClientAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var connector = new TcpConnector();
        if (!await connector.ConnectAsync(host, port, options, ct))
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            continue;
        }

        var session = new TcpSession(connector.Channel!, converter, s => new ChatHandler(s));
        var tcs = new TaskCompletionSource<DisconnectReason>();

        session.Disconnected += (_, e) => tcs.TrySetResult(e.Reason);

        await session.SendAsync(new ChatMessage { Text = "hello" });
        // ... 채팅 루프 ...

        var reason = await tcs.Task;
        session.Dispose();

        if (reason == DisconnectReason.Local)
            break; // 사용자 종료 — 재접속 안 함

        await Task.Delay(TimeSpan.FromSeconds(2), ct); // backoff
    }
}
```

서버 측: Accept → Session B 생성 → 첫 메시지에 `reconnectToken` → 앱 `PlayerRegistry`에서 유저에 Session B 장착, Session A 정리.

## 5. RUDP

```csharp
using System.Net;
using Communication.Network.RUDP;
using Communication.Shared.Sessions;

// 서버 — 수락된 채널 위에 앱이 세션을 생성한다 (TCP와 동일 원칙)
using var listener = new RudpListener(IPAddress.Any, 32000);
listener.Accepted += channel =>
{
    var session = new RudpSession(channel, converter, s => new ChatHandler(s));
    session.Disconnected += (_, e) => Console.WriteLine($"client left: {e.Reason}");
};
listener.Start(new RudpTransportOptions { MaxConnections = 100 });
Console.WriteLine($"listening on {listener.LocalPort}");

// 클라이언트
var connector = new RudpConnector();
if (!await connector.ConnectAsync("127.0.0.1", 32000)) return;
var session = new RudpSession(connector.Channel!, converter, s => new ChatHandler(s));

// 메시지별로 전송 방식을 다르게 — RudpSendOptions
await session.SendAsync(chat,     RudpSendOptions.ReliableOrdered);   // 공용 인스턴스 — 할당 0
await session.SendAsync(position, RudpSendOptions.Unreliable);        // 빈도 높은 상태 동기화
await session.SendAndFlushAsync(important, new RudpSendOptions(RudpDeliveryMethod.ReliableSequenced));
```

- 옵션을 넘기지 않으면 **`ReliableOrdered`**로 간다.
- 분할 불가 방식(`Sequenced`·`ReliableSequenced`·`Unreliable`)으로 MTU 초과 payload를 보내면 `ArgumentException`이 나고 **세션이 `Disconnected(Error)`로 끊긴다** — 큰 메시지는 `ReliableOrdered`/`ReliableUnordered`로.
- 클라이언트는 세션(또는 채널)만 Dispose하면 내부 폴링 스레드·NetManager까지 정리된다. 서버는 `listener.Stop()`이 접속 중 peer에 끊김 메시지를 보낸다.
- 접속 수와 무관하게 호스트당 폴링 스레드 1개 — [[../05-Decisions/0007-rudp-three-way-split-and-polling|ADR 0007]].
- 실행 검증: `dotnet run --project Sandbox/Chat.RUDP -- --selftest` (5개 전송 방식 왕복 후 exit 0), 채팅은 `server [port]` / `client [port] [이름]` — `'!'` 접두 줄은 Unreliable로 전송.

## 6. 앱 하트비트

일반 메시지 + 앱 타이머. 타임아웃 시 `session.Disconnect()` → `Disconnected(Local)` 또는 앱이 `Remote`로 간주하고 재접속 루프.

## 관련

- [[../03-Reference/Configuration|Configuration]] · [[../03-Reference/Public-API|Public-API]] · [[Implementation-Roadmap]]
