# TCP Transport Guide

TCP is the byte-stream stack: `TcpListener` (accept loop) and `TcpConnector` (connect) hand out `IByteChannel`s, the application wraps each channel in a `TcpSession`, and the shared `MessagePipeline` adds 4-byte little-endian length-prefix framing on top. All types live in the `Communication.Network.TCP` namespace, spread across the `Communication.Network.TCP.Shared` / `.Server` / `.Client` packages.

Package installs:

```console
dotnet add package Communication.Network.TCP.Server   # TcpListener
dotnet add package Communication.Network.TCP.Client   # TcpConnector
```

See [README.md](README.md) for the shared feature set (sessions, converter injection, framing, queueing). This page covers TCP-specific behavior.

## Types at a glance

| Type | Package | Purpose |
| --- | --- | --- |
| `TcpListener` | TCP.Server | Accept loop; raises `Accepted(IByteChannel)` |
| `TcpConnector` | TCP.Client | `ConnectAsync` → `bool`; exposes `Channel` |
| `TcpSession` | TCP.Shared | App-created session over an `IByteChannel` |
| `StreamByteChannel` | TCP.Shared | `TcpClient`/`NetworkStream`/`SslStream` → `IByteChannel` |
| `TcpTransportOptions` | TCP.Shared | Keep-alive, `NoDelay`, `MaxConnections`, `ConnectTimeout`, `Tls` |
| `TcpTlsOptions` | TCP.Shared | TLS (`SslStream`) options |

## TcpListener — the accept loop

```csharp
using System.Net;
using Communication.Network.TCP;

using var listener = new TcpListener(IPAddress.Any, 32000);
listener.Accepted += channel =>
{
    // Session creation is always the app's job.
    var session = new TcpSession(channel, converter, s => new ChatHandler(s));
    session.Disconnected += (_, e) => Console.WriteLine($"peer left: {e.Reason}");
};
listener.Start(new TcpTransportOptions { MaxConnections = 1000 });

int active = listener.ActiveConnectionCount; // accepted, not-yet-disposed channels
// EndPoint? LocalEndpoint — actual bound endpoint; useful with port 0.
```

Constructor: `TcpListener(IPAddress address, int port)`. Members:

- `event Action<IByteChannel>? Accepted` — delivered per accepted connection.
- `void Start(TcpTransportOptions? options = null)` — binds and starts the accept loop.
- `void Stop()` / `void Dispose()` — stop accepting. Already-accepted channels belong to their sessions and are unaffected.
- `EndPoint? LocalEndpoint` — bound endpoint (`Start` required).
- `int ActiveConnectionCount` — accepted channels not yet disposed; the basis of `MaxConnections` enforcement.

Behavior:

- **Subscription timing** — `Accepted` reads the latest subscriber on every accept, so handlers subscribed after `Start` still receive channels.
- **No-subscriber / handler-exception isolation** — if nobody subscribed, or an `Accepted` handler throws, the channel is disposed and the accept loop continues. A failed handler can never leak connections or kill the loop.
- **MaxConnections** — when the count of undisposed channels reaches the limit, new connections are closed immediately and accepting continues. The slot is reclaimed when the channel (usually via its session) is disposed. `0`/negative values are rejected.
- **Socket options before wrapping** — `NoDelay` and keep-alive are applied to the raw socket right after accept. If the peer already reset the connection, that one connection is dropped; the loop survives.
- **TLS** — with `TcpTlsOptions.ServerCertificate` set, each connection runs its TLS handshake on a dedicated task (the accept loop is never blocked by a silent client); only successfully authenticated connections reach `Accepted`. Failures and `HandshakeTimeout` overruns close the connection, reclaim the slot, and keep accepting.
- **Transient accept errors** — logged (Trace) and retried after 50 ms instead of silently ending acceptance.

## TcpConnector

```csharp
using Communication.Network.TCP;

var connector = new TcpConnector();
if (!await connector.ConnectAsync("game.example.com", 32000)) return; // false = failed

using var session = new TcpSession(connector.Channel!, converter, s => new ChatHandler(s));
```

Signature: `Task<bool> ConnectAsync(string host, int port, TcpTransportOptions? options = null, CancellationToken cancellationToken = default)`. On success `Channel` (an `IByteChannel?`) is set; on failure it stays `null`.

- Cancellation throws `OperationCanceledException`; a `ConnectTimeout` instead returns `false`. These are independent.
- With `Tls` set, the client-side handshake completes before `Channel` is exposed. Handshake failure (rejected certificate, protocol violation, timeout) resolves to `false`.
- Socket option (`NoDelay`, keep-alive) failures after connect also resolve to `false` with the connection cleaned up — no leaks, no thrown exceptions.

## TcpSession

```csharp
public TcpSession(
    IByteChannel channel,
    IMessageConverter converter,
    Func<ISession, IMessageHandler> handlerFactory,
    MessageQueueOptions? queueOptions = null)
```

The session owns the channel (disposing the session closes the connection) and the pipeline. All send/receive/disconnect semantics are the shared `Session`/`MessagePipeline` behavior described in [README.md](README.md#features). Nothing TCP-specific leaks into the session API.

## StreamByteChannel

`StreamByteChannel : IByteChannel` adapts a connected stream. You normally never construct it — the listener and connector produce it:

- `new StreamByteChannel(TcpClient client)` — owns and disposes the client.
- `new StreamByteChannel(Stream stream, Socket socket)` — wraps an already-established stream (this is the TLS path: `stream` is the authenticated `SslStream`; disposing it closes the underlying socket).
- `Socket Socket` — the underlying socket, used for keep-alive/`NoDelay` application.
- `ReadAsync` returning `0` means clean EOF (remote close); exceptions surface as channel errors → `Disconnected(Error)`.

## TcpTransportOptions

```csharp
var options = new TcpTransportOptions
{
    KeepAlive = new SocketKeepAliveOptions   // null = leave OS defaults
    {
        Enabled = true,
        IdleTime = TimeSpan.FromSeconds(30), // first probe after idle (Zero = OS default)
        Interval = TimeSpan.FromSeconds(5),  // probe interval          (Zero = OS default)
    },
    NoDelay = true,          // default true: TCP_NODELAY — coalescing already batches writes
    MaxConnections = 1000,   // int?, null = unlimited (listener)
    ConnectTimeout = 5000,   // int? ms, null = OS default (connector)
    Tls = null,              // TcpTlsOptions?, null = plain text
};
```

| Option | Default | Applies to | Semantics |
| --- | --- | --- | --- |
| `KeepAlive` | `null` | both | `SocketKeepAliveOptions`: `Enabled` (`false` ignores the whole block), `IdleTime`, `Interval`. Windows uses `SIO_KEEPALIVE_VALS`; Unix uses raw `TCP_KEEPIDLE`/`TCP_KEEPINTVL` (macOS idle `TCP_KEEPALIVE`). Unsupported fields are quietly ignored — a half-open detection aid, not a heartbeat. |
| `NoDelay` | `true` | both | Disables Nagle. The library already coalesces sends (default 64 KB batches), so Nagle would only add latency. `false` keeps the OS setting. |
| `MaxConnections` | `null` | listener | Concurrent accepted-connection cap. Over-limit connections close immediately; accepting continues. Slots are reclaimed on channel dispose. `null` = unlimited; `0`/negative rejected. |
| `ConnectTimeout` | `null` | connector | Upper bound (ms) on the connect attempt. Without it, a silent host rides OS SYN retries for tens of seconds (≈21 s on Windows). Timeout → `false`. Independent from cancellation. `0`/negative rejected. |
| `Tls` | `null` | both | `TcpTlsOptions`; `null` keeps plain-text behavior (backward compatible). |

## Security: TLS via SslStream

TLS is enabled per side from the same `TcpTlsOptions` instance:

```csharp
// Server — set ServerCertificate; every accepted connection must complete
// the handshake before it reaches Accepted.
var serverOptions = new TcpTransportOptions
{
    Tls = new TcpTlsOptions
    {
        ServerCertificate = serverCert,          // X509Certificate, listener-only
        HandshakeTimeout = 15_000,               // ms, default 15_000
    },
};
listener.Start(serverOptions);

// Client — setting Tls at all turns TLS on. Default validation is the OS
// trust chain + name match (TargetHost defaults to the ConnectAsync host).
var clientOptions = new TcpTransportOptions
{
    Tls = new TcpTlsOptions
    {
        // TargetHost = "game.example.com",       // optional; empty string throws
        // RemoteCertificateValidation = ...      // optional override; null = OS default
    },
};
bool ok = await connector.ConnectAsync("game.example.com", 32000, clientOptions);
```

| Option | Default | Side | Semantics |
| --- | --- | --- | --- |
| `ServerCertificate` | `null` | listener | `X509Certificate?`. Handshake runs per connection on its own task; success is required before `Accepted`. Failures/timeouts close the connection, reclaim the slot, keep accepting. Unused on the client. |
| `TargetHost` | `null` | client | Name used for SNI/certificate name validation. `null` → the `ConnectAsync` `host` argument. Empty string throws `ArgumentException`. |
| `RemoteCertificateValidation` | `null` | client | `RemoteCertificateValidationCallback?`. `null` → OS default validation (self-signed certificates are rejected). Never install an always-true callback — that is a man-in-the-middle hole; check fingerprints/chains instead. |
| `HandshakeTimeout` | `15000` | both | Handshake ceiling in ms (slowloris defense; `netstandard2.1` `SslStream` has no cancellation, so it is a timeout race + stream disposal). Server: close connection, reclaim slot. Client: connect fails (`false`). `0`/negative rejected. |

Notes:

- The handshake happens before any framing, so sessions and pipelines work unchanged on top of the authenticated stream.
- **TLS 1.3 caveat**: a client that rejects the certificate may still produce a server-side `Accepted`, because Schannel reports validation failure after completing the handshake. Whoever accepts owns and must eventually dispose the channel/session, as always.
- The `SslProtocols` selection is left to the OS (`SslProtocols.None`), and revocation checking is not performed (`checkCertificateRevocation: false`).

Full contract: [ADR 0008](Document/05-Decisions/0008-tcp-tls-sslstream.md) · [Document/03-Reference/Configuration.md](Document/03-Reference/Configuration.md).

## Lifecycle and threading model

- **Accept loop** — one background task inside `TcpListener`. Per connection: socket options → `MaxConnections` slot reservation → (optional TLS handshake on a separate task) → `Accepted`. `Stop()`/`Dispose()` cancels it; in-flight TLS completions after stop are discarded.
- **Session loops** — `TcpSession` attaches a `MessagePipeline`, which runs a send loop and a receive loop on pool threads. Sends are queued, coalesced into batched writes (default 64 KB), and flushed per `SendAndFlushAsync`.
- **Handlers** — run on the pipeline's dispatch loop (`InlineDispatch = false`, default) or inline on the receive loop (`InlineDispatch = true`, hot path only). One session's slow handler never blocks other sessions — TCP sessions have independent loops.
- **Disconnect** —
  - `Disconnect()` / `Dispose()` → `Disconnected(Local)`;
  - clean EOF at a frame boundary → `Remote`;
  - I/O error, mid-frame EOF, malformed frame length → `Error` (with the exception in `DisconnectedEventArgs.Exception`);
  - incomplete frame after `FrameTimeout` (default 30 s) → `Timeout`.
- The `Disconnected` event fires exactly once, after the pipeline and channel are disposed; late subscribers get an immediate one-shot replay. Reconnect is the app's job: `ConnectAsync` again, `new TcpSession` again.

## Complete end-to-end example

Runnable as-is (single process starts the server on an ephemeral port, connects, round-trips one message):

```csharp
using System.Buffers;
using System.Net;
using System.Text.Json;
using Communication.Network.TCP;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;

// --- server ---
using var listener = new TcpListener(IPAddress.Any, 0); // port 0 = ephemeral
listener.Accepted += channel =>
{
    var session = new TcpSession(channel, new JsonChatConverter(), s => new ChatHandler(s));
    session.Disconnected += (_, e) => Console.WriteLine($"peer left: {e.Reason}");
};
listener.Start(new TcpTransportOptions { NoDelay = true, MaxConnections = 100 });
int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

// --- client (TLS variant: add Tls = new TcpTlsOptions() on both ends) ---
var connector = new TcpConnector();
if (!await connector.ConnectAsync("127.0.0.1", port, new TcpTransportOptions
    {
        ConnectTimeout = 5_000,
        KeepAlive = new SocketKeepAliveOptions { Enabled = true, IdleTime = TimeSpan.FromSeconds(30) },
    }))
{
    return; // connect failed
}

using var client = new TcpSession(connector.Channel!, new JsonChatConverter(), s => new ChatHandler(s));
client.Disconnected += (_, e) => Console.WriteLine($"disconnected: {e.Reason}");

await client.SendAndFlushAsync(new ChatMessage { Text = "hello" });
client.Disconnect(); // -> Disconnected(Local) on this side, Remote on the server

listener.Stop();

// --- app contract: message, converter, handler ---
// Type declarations follow the top-level statements so this file stays compilable as-is.
public sealed class ChatMessage { public string Text { get; set; } = ""; }

public sealed class JsonChatConverter : IMessageConverter
{
    public void Serialize(object message, IBufferWriter<byte> writer) =>
        writer.Write(JsonSerializer.SerializeToUtf8Bytes(message, message.GetType()));

    public object Deserialize(ReadOnlySpan<byte> message) =>
        JsonSerializer.Deserialize<ChatMessage>(message) ?? new ChatMessage();
}

public sealed class ChatHandler : MessageHandler
{
    public ChatHandler(ISession session) : base(session) =>
        Register<ChatMessage>(m => Console.WriteLine($"recv: {m.Text}"));
}
```

For a manually verifiable sample, run [`Sandbox/Chat.TCP`](Sandbox/Chat.TCP/) (`server [port]` / `client [port] [name]`, or `--selftest` for an in-process round-trip check).

## See also

- [RUDP.md](RUDP.md) — the message-oriented UDP stack
- [Document/02-Architecture/Overview.md](Document/02-Architecture/Overview.md) · [Session](Document/02-Architecture/Session.md) · [Pipeline](Document/02-Architecture/Pipeline.md)
- [Document/04-Guides/Getting-Started.md](Document/04-Guides/Getting-Started.md) — keep-alive, reconnect loops, app heartbeats
