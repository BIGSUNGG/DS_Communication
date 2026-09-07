---
project: DS_Communication
type: guide
status: draft
tags: [guide, usage, examples]
updated: 2026-09-09
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

## 6. 전송 보안 옵션 — TCP TLS · RUDP CRC32c

### TCP TLS (SslStream)

```csharp
// 서버 — 인증서 설정 시 모든 수락 연결에 대해 핸드셰이크를 먼저 완료한 뒤 Accepted가 온다
listener.Start(new TcpTransportOptions
{
    Tls = new TcpTlsOptions { ServerCertificate = serverCert }, // X509Certificate
});

// 클라이언트 — 옵션만 설정하면 연결 후 핸드셰이크까지 마친 뒤 채널 노출
bool ok = await connector.ConnectAsync("game.example.com", 32000, new TcpTransportOptions
{
    Tls = new TcpTlsOptions(), // 기본 OS 검증(신뢰 체인·이름 일치). TargetHost 기본 = host 인자
});
```

- 핸드셰이크 실패(인증서 거부·프로토콜 위반)와 `HandshakeTimeout`(기본 15초) 초과는 — 클라이언트는 **연결 실패(`false`)**, 서버는 **연결 폐기 후 수락 계속**(상한 슬롯도 회수). 세션·프레이밍은 그대로.
- 개발용 자체 서명 인증서는 `RemoteCertificateValidation` 콜백에서 수용(지문 검사 권장) — **무조건 통과 콜백 금지(중간자 공격)**.
- TLS 1.3 주의: 클라이언트가 인증서를 거부해도 서버 쪽 핸드셰이크는 완료돼 `Accepted`가 발생할 수 있다 — 수용 핸들러는 언제나처럼 채널(세션)을 소유·정리.
- 상세 계약: [[../05-Decisions/0008-tcp-tls-sslstream|ADR 0008]] · [[Security|Security & Production Checklist]]

### RUDP 패킷 무결성 (CRC32c)

```csharp
var options = new RudpTransportOptions { Crc32cEnabled = true }; // 양단 같은 설정 — 와이어 비호환
listener.Start(options);
bool ok = await connector.ConnectAsync("127.0.0.1", 32000, options);
```

- 패킷마다 CRC32c(4바이트) — 체크섬 위반 패킷(손상·위조)을 **프로토콜 처리 전에 폐기**한다(위조 접속 요청은 슬롯 예약도 없이 버림). IPv4 UDP 체크섬은 0일 수 있어 이 선별이 유일한 방어선이 될 수 있다.
- **검출 전용**이다 — 키 없는 CRC라 능동 공격자는 재계산할 수 있다. 기밀성·인증은 없다(평문 유지): 기밀성은 TCP TLS 또는 VPN 위 운용으로 확보.

## 7. 앱 하트비트

일반 메시지 + 앱 타이머. 타임아웃 시 `session.Disconnect()` → `Disconnected(Local)` 또는 앱이 `Remote`로 간주하고 재접속 루프.

## 관련

- [[../03-Reference/Configuration|Configuration]] · [[../03-Reference/Public-API|Public-API]] · [[Implementation-Roadmap]]
