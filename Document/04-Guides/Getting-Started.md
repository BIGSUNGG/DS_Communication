---
project: DS_Communication
type: guide
status: draft
tags: [guide, usage, examples]
updated: 2026-08-31
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
var session = new RudpSession(connector.Channel, converter, s => new ChatHandler(s));
await session.SendAsync(msg, new RudpSendOptions { ReliableType = RudpReliableType.ReliableOrdered });
```

## 6. 앱 하트비트

일반 메시지 + 앱 타이머. 타임아웃 시 `session.Disconnect()` → `Disconnected(Local)` 또는 앱이 `Remote`로 간주하고 재접속 루프.

## 관련

- [[../03-Reference/Configuration|Configuration]] · [[../03-Reference/Public-API|Public-API]] · [[Implementation-Roadmap]]
