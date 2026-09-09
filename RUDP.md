# RUDP Transport Guide

RUDP is the message-oriented UDP stack: `RudpListener` and `RudpConnector` hand out `IMessageChannel`s with message boundaries preserved by the transport (no length-prefix framer needed), the application wraps each channel in a `RudpSession`, and each message carries its own `RudpSendOptions` delivery choice. All types live in the `Communication.Network.RUDP` namespace across the `Communication.Network.RUDP.Shared` / `.Server` / `.Client` packages. Under the hood it is LiteNetLib 2.1.4 — its types are hidden and never appear in the public API.

Package installs:

```console
dotnet add package Communication.Network.RUDP.Server   # RudpListener
dotnet add package Communication.Network.RUDP.Client   # RudpConnector
```

See [README.md](README.md) for the shared feature set (sessions, converter injection, queueing). This page covers RUDP-specific behavior.

## Types at a glance

| Type | Package | Purpose |
| --- | --- | --- |
| `RudpListener` | RUDP.Server | Accept loop; raises `Accepted(IMessageChannel)` |
| `RudpConnector` | RUDP.Client | `ConnectAsync` → `bool`; exposes `Channel` |
| `RudpSession` | RUDP.Shared | App-created session over an `IMessageChannel` |
| `RudpMessageChannel` | RUDP.Shared | `IMessageChannel` over one LiteNetLib peer (you receive it, never construct it) |
| `RudpSendOptions` / `RudpDeliveryMethod` | RUDP.Shared | Per-message delivery options |
| `RudpTransportOptions` | RUDP.Shared | `MaxConnections`, `DisconnectTimeout`, `ConnectionKey`, `IPv6`, `Crc32cEnabled`, `ConnectTimeout`, `Tls` |
| `RudpTlsOptions` | RUDP.Shared | DTLS 1.2 (BouncyCastle) options |

Internal (not part of the public API, listed for orientation): `RudpNetHost` (owns the LiteNetLib `NetManager`, the polling thread, and the accept policy), `RudpDtlsHandshake` (DTLS 1.2 handshake runner), `RudpDtlsTransport` (datagram transport adapter), `RudpTlsChannel` (encrypted `IMessageChannel` wrapper with chunking).

## RudpListener

```csharp
using System.Net;
using Communication.Network.RUDP;

using var listener = new RudpListener(IPAddress.Any, 32000);
listener.Accepted += channel =>
{
    // IMPORTANT: create the session synchronously inside the callback.
    var session = new RudpSession(channel, converter, s => new ChatHandler(s));
    session.Disconnected += (_, e) => Console.WriteLine($"client left: {e.Reason}");
};
listener.Start(new RudpTransportOptions { MaxConnections = 100 });
Console.WriteLine(listener.LocalPort);
```

Constructor: `RudpListener(IPAddress address, int port)`. Members:

- `event Action<IMessageChannel>? Accepted` — delivered per accepted peer.
- `void Start(RudpTransportOptions? options = null)` — binds and starts the polling thread. Throws `InvalidOperationException` on double start or bind failure.
- `void Stop()` / `void Dispose()` — stops the host with `NetManager.Stop(true)`: connected peers receive a disconnect message immediately (they see a remote close instead of waiting out `DisconnectTimeout`).
- `int LocalPort` — actual bound port (port 0 binds an ephemeral port).
- `int ActiveConnectionCount` — accepted channels not yet reclaimed; the `MaxConnections` enforcement basis.

Behavior:

- **Create the session synchronously in `Accepted`.** Message channels do not buffer messages that arrive before you subscribe, so deferring session creation to another thread loses whatever arrives in between. TCP has no such window (the stream buffers). Like `TcpListener`, `Accepted` reads the latest subscriber per accept, so subscribing after `Start` is fine.
- **MaxConnections** — connection-request slots are *reserved before acceptance*, so a burst of requests in one polling batch cannot jointly overshoot the cap. Over-limit or wrong-key requests are rejected and never produce `Accepted`. Slots are reclaimed when the peer disconnects or the channel is disposed.
- **Connection key** — requests are accepted only when the key matches. Starting with the public default key logs a Trace warning (see [Security](#security-crc32c-connection-key-and-dtls)).

## RudpConnector

```csharp
using Communication.Network.RUDP;

var connector = new RudpConnector();
if (!await connector.ConnectAsync("127.0.0.1", 32000,
        new RudpTransportOptions { ConnectionKey = "my-app-key", ConnectTimeout = 3_000 })) return;

using var session = new RudpSession(connector.Channel!, converter, s => new ChatHandler(s));
```

Signature: `Task<bool> ConnectAsync(string host, int port, RudpTransportOptions? options = null, CancellationToken cancellationToken = default)`. On success `Channel` (an `IMessageChannel?`) is set; on failure it stays `null`.

- Failure modes resolved as `false`: connection rejected (wrong key or server cap), host unresolvable, retries exhausted or `ConnectTimeout` exceeded.
- Cancellation throws `OperationCanceledException` and disposes the host (an in-flight UDP connect cannot be interrupted, so the wait is cancelled instead).
- The client host **rejects all incoming connection requests** — reverse connections onto its ephemeral port are impossible.
- **Ownership**: the client has exactly one peer, so its channel owns the internal host (polling thread + `NetManager`). Disposing the session or channel cleans everything up with no leftovers. Server-side channels do *not* own the shared host; `RudpListener.Stop()` owns that.

## RudpSession

```csharp
public RudpSession(
    IMessageChannel channel,
    IMessageConverter converter,
    Func<ISession, IMessageHandler> handlerFactory,
    MessageQueueOptions? queueOptions = null)
```

Same shape and ownership as `TcpSession`, but over an `IMessageChannel`. RUDP-specific points:

- **Remote disconnect wiring**: the message-channel path has no receive loop that could observe EOF, so `RudpSession` subscribes to the channel's transport-disconnect notification (and recovers a disconnect that latched before subscription — e.g. in the accept→session-creation window) and surfaces it through the standard `Session.Disconnected` event. Reason mapping from LiteNetLib: `Timeout` → `Timeout`, `RemoteConnectionClose` → `Remote`, `DisconnectPeerCalled` → `Local`, everything else → `Error`. These notifications carry a `null` `DisconnectedEventArgs.Exception`.
- `MessageQueueOptions.InlineDispatch` is **ignored** on this path — receive callbacks fire on the shared polling thread, so handlers always run on the session's dispatch queue (see [Threading model](#threading-model-polling-thread-and-dispatch-queues)).

## RudpSendOptions and RudpDeliveryMethod

```csharp
public enum RudpDeliveryMethod
{
    ReliableUnordered = 0, // no loss, no duplicates, no ordering   (fragmentable)
    Sequenced         = 1, // may drop, no duplicates, in order; stale packets dropped (not fragmentable)
    ReliableOrdered   = 2, // no loss, no duplicates, in order    (fragmentable) — default
    ReliableSequenced = 3, // only the latest packet is reliable; cannot be fragmented
    Unreliable        = 4, // may drop, may duplicate, no ordering (not fragmentable)
}

public sealed class RudpSendOptions : SendOptions
{
    public RudpSendOptions(RudpDeliveryMethod deliveryMethod);
    public RudpDeliveryMethod DeliveryMethod { get; }

    // Shared instances — zero allocation on the send path:
    public static RudpSendOptions ReliableOrdered { get; }
    public static RudpSendOptions ReliableUnordered { get; }
    public static RudpSendOptions Sequenced { get; }
    public static RudpSendOptions ReliableSequenced { get; }
    public static RudpSendOptions Unreliable { get; }
}
```

Semantics per value:

| Value | Loss | Duplicates | Order | Typical use |
| --- | --- | --- | --- | --- |
| `ReliableOrdered` (default) | never | never | guaranteed | chat, commands, anything large (only fragmentable-with-chunking under TLS) |
| `ReliableUnordered` | never | never | not guaranteed | reliable state chunks where order doesn't matter |
| `Sequenced` | possible (stale dropped) | never | guaranteed (latest wins) | frequent snapshots where only the newest counts |
| `ReliableSequenced` | older packets may drop; latest is retransmitted until received | never | guaranteed (latest wins) | "latest state, guaranteed eventually" (e.g. final position) |
| `Unreliable` | possible | possible | none | high-rate throwaway telemetry |

Usage:

```csharp
await session.SendAsync(message, RudpSendOptions.ReliableOrdered);  // shared instance
await session.SendAsync(message, new RudpSendOptions(RudpDeliveryMethod.ReliableSequenced));
await session.SendAsync(message);                                   // null / non-RudpSendOptions -> ReliableOrdered
```

**MTU guard** — for the non-fragmenting methods (`Sequenced`, `ReliableSequenced`, `Unreliable`), a payload larger than the peer's max single packet size fails immediately with `ArgumentException` (silent loss is avoided). Because the pipeline treats this as a channel error, that send's flush faults and the session disconnects with `Disconnected(Error)` (the exception is preserved in `DisconnectedEventArgs.Exception`). Fragmentable methods (`ReliableOrdered`, `ReliableUnordered`) have no such limit; payloads beyond `MessageQueueOptions.MaxFrameLength` (default 4 MB) are isolated per-item before reaching the channel. Size-limit your application messages.

## RudpTransportOptions

```csharp
var options = new RudpTransportOptions
{
    MaxConnections = 100,        // int?, null = unlimited (server side only)
    DisconnectTimeout = 5_000,   // ms, default 5000 — the only half-open signal on UDP
    ConnectionKey = "my-app-key",// default "DS_Communication.RUDP" (public constant)
    IPv6 = false,                // default false — IPv4 only
    Crc32cEnabled = false,       // default false — packet integrity layer, both ends must match
    ConnectTimeout = 3_000,      // int? ms, null = LiteNetLib default (~5 s), client side only
    Tls = null,                  // RudpTlsOptions?, null = plain text, both ends must match
};
listener.Start(options);
// or
await connector.ConnectAsync(host, port, options);
```

| Option | Default | Semantics |
| --- | --- | --- |
| `MaxConnections` | `null` | Concurrent accepted-peer cap (server side). Slots are reserved at connection-request time, so a same-batch burst cannot overshoot; over-limit requests are rejected with no `Accepted`. Reclaimed on peer disconnect / channel dispose. `null` = unlimited; `0`/negative rejected. |
| `DisconnectTimeout` | `5000` | Silence duration (ms) after which a peer is considered disconnected. UDP has no stream end, so this is the **only** half-open detection signal — coordinate with your app heartbeat. `0`/negative rejected. |
| `ConnectionKey` | `"DS_Communication.RUDP"` | Connection-request validation key: the server accepts only matching requests, the client connects with it. `null`/empty rejected. Default is a public constant — replace it on public networks (a Trace warning reminds you at server start). |
| `IPv6` | `false` | Binds an IPv6 socket in addition to IPv4. |
| `Crc32cEnabled` | `false` | CRC32c checksum (4 bytes) per packet; violating packets are discarded before protocol processing. **Both ends must use the same setting** (wire-incompatible). Detection only — an unkeyed CRC is not protection against an active attacker. |
| `ConnectTimeout` | `null` | Client-side ceiling (ms) on the connect attempt (LiteNetLib's own retry budget is ~5 s fixed). `null` keeps that default. No effect on the server. |
| `Tls` | `null` | DTLS 1.2 options (`RudpTlsOptions`). Handshake runs after the LiteNetLib key handshake, before the channel is delivered. **Both ends must use the same setting.** |

## Security: CRC32c, connection key, and DTLS

- **CRC32c packet integrity** — `Crc32cEnabled = true` adds a per-packet CRC32c. Corrupted or forged packets (including forged connection requests, which never reserve a slot) are dropped before protocol processing. IPv4 UDP checksums can legally be zero, so this can be your only corrupted-packet filter. It is a detection layer: keyed/authenticated protection requires DTLS or a VPN.
- **Connection key** — a plain shared-secret filter on connection requests. The default value is public; treat it as a spam filter, not authentication, and replace it per app in production.
- **DTLS 1.2 (`RudpTlsOptions`)** — BouncyCastle.Cryptography 2.7.0 (referenced only by `Communication.Network.RUDP.Shared`; BouncyCastle types are not exposed). Enabled by setting `RudpTransportOptions.Tls` on either side.

```csharp
// Server — certificate must contain a private key (RSA-2048+ or ECDSA P-256).
using X509Certificate2 certificate = LoadServerCertificate();
listener.Start(new RudpTransportOptions
{
    Tls = new RudpTlsOptions { ServerCertificate = certificate },
});

// Client — pinning is the standard route for game servers (no public CA/domain needed):
byte[] pinnedDer = LoadPinnedCertificateDer();
string expected = RudpTlsOptions.GetSha256Fingerprint(pinnedDer); // hex, colon-separated
await connector.ConnectAsync(host, port, new RudpTransportOptions
{
    Tls = new RudpTlsOptions
    {
        RemoteCertificateValidation = der => RudpTlsOptions.GetSha256Fingerprint(der) == expected,
        // or: TargetHost = "game.example.com"  (SAN/CN match; ignored when the callback is set)
    },
});
```

| Option | Default | Side | Semantics |
| --- | --- | --- | --- |
| `ServerCertificate` | `null` | listener | `X509Certificate2?` with private key required. Handshake failures/timeouts dispose the channel and reclaim the slot; accepting continues. |
| `TargetHost` | `null` | client | SAN(dNSName)/CN case-insensitive name match. Empty string throws `ArgumentException`. |
| `RemoteCertificateValidation` | `null` | client | `RudpRemoteCertificateValidation?` receives the server certificate DER and returns `true` to continue. **Fail-closed**: with neither this nor `TargetHost` set, the server certificate is rejected by default. Never return `true` unconditionally. |
| `HandshakeTimeout` | `15000` | both | DTLS handshake ceiling in ms (slowloris defense). Server: channel discarded + slot reclaimed. Client: connect failure. `0`/negative rejected. |
| `GetSha256Fingerprint(byte[] certificateDer)` | — | static | SHA-256 fingerprint as colon-separated hex — the pinning comparison helper. |

Behavior notes:

- The handshake runs on a dedicated task per connection (never on the polling thread) after the LiteNetLib connection is established; only authenticated channels reach `Accepted`/`Channel`.
- Record-level encryption is ECDHE + AES-GCM (cipher suite chosen by certificate key type); DTLS 1.2 is fixed.
- **Message boundaries survive TLS** via an internal 3-byte envelope (`[flags][len]`) plus chunking: messages over 16,381 bytes travel only as `ReliableOrdered` (multiple records, reassembled on receive, 64 MB reassembly ceiling); other methods over that size fail fast with `ArgumentException` (same philosophy as the MTU guard). Envelope violations on receive are fail-closed — the channel is disposed and the session disconnects.
- Decryption happens on a per-connection asynchronous pump, not the polling thread, so crypto cost on one connection never stalls the others.
- About ~5% overhead at 512 B echo round-trips in the built-in benchmark (`Chat.RUDP --bench --tls`).

Full contract: [ADR 0009](Document/05-Decisions/0009-rudp-tls-dtls.md) · [Document/04-Guides/Security.md](Document/04-Guides/Security.md).

## Threading model: polling thread and dispatch queues

- **One dedicated polling thread per host** (`RudpNetHost`): each `RudpListener` and each `RudpConnector` runs exactly one background thread that drains LiteNetLib `PollEvents()` on a fixed 1 ms interval. Thread count is independent of the number of connections; the interval is deliberately not an option.
- Receive callbacks (`IMessageChannel.MessageReceived`) fire on that polling thread, and payloads are valid **only inside the callback** (the pipeline deserializes within it).
- **Handlers never run on the polling thread.** Deserialized messages are handed to the per-session dispatch queue in `MessagePipeline` (`InlineDispatch` is forced to queued on this path), so one slow client's handler cannot stall other sessions' receives, accepts, or the cap enforcement.
- LiteNetLib's `UnsyncedEvents` stays `false` (events are queued and drained by the polling thread) — this is the structural guarantee above; it is not exposed as an option.
- Lifecycle costs: per host, one polling thread plus LiteNetLib's own internal threads; per TLS connection, an idle-cost-free pump task.

## Why the three-package split

Like TCP, RUDP ships as `Shared` / `Server` / `Client` (ADR 0007), amending the original "one package per stack" rule:

- **Independent install** — a server app does not need connector code on disk and vice versa; `Server` and `Client` reference only `.Shared`.
- **Third-party containment** — LiteNetLib (and BouncyCastle for TLS) is referenced *only* by `Communication.Network.RUDP.Shared` and reaches `Server`/`Client` transitively. Third-party types appear in no public signature, so the interim LiteNetLib implementation can later be replaced by an in-house RUDP without breaking application code (ADR 0005 exit strategy).
- All three assemblies share the single `Communication.Network.RUDP` namespace; `Server`/`Client` access internal host types via `InternalsVisibleTo`.

## Complete end-to-end example

Runnable as-is (single process, ephemeral port, three of the five delivery methods round-tripped; the sandbox's `--selftest` exercises all five):

```csharp
using System.Buffers;
using System.Net;
using System.Text.Json;
using Communication.Network.RUDP;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;

// --- server ---
using var listener = new RudpListener(IPAddress.Loopback, 0);
listener.Accepted += channel =>
{
    // Synchronous session creation — message channels do not buffer pre-subscription messages.
    var session = new RudpSession(channel, new JsonChatConverter(), s => new ChatHandler(s));
    session.Disconnected += (_, e) => Console.WriteLine($"client left: {e.Reason}");
};
listener.Start(new RudpTransportOptions
{
    MaxConnections = 100,
    ConnectionKey = "my-app-key",
    Crc32cEnabled = true, // optional integrity layer — both ends must match
});
int port = listener.LocalPort;

// --- client ---
var connector = new RudpConnector();
if (!await connector.ConnectAsync("127.0.0.1", port, new RudpTransportOptions
    {
        ConnectionKey = "my-app-key",
        Crc32cEnabled = true,
        ConnectTimeout = 3_000,
        // Tls = new RudpTlsOptions { RemoteCertificateValidation = ... }, // optional DTLS
    }))
{
    return; // rejected, unreachable, or timed out
}

using var client = new RudpSession(connector.Channel!, new JsonChatConverter(), s => new ChatHandler(s));
client.Disconnected += (_, e) => Console.WriteLine($"disconnected: {e.Reason}");

// Per-message delivery:
await client.SendAndFlushAsync(new ChatMessage { Text = "reliable" }, RudpSendOptions.ReliableOrdered);
await client.SendAndFlushAsync(new ChatMessage { Text = "snapshot" }, RudpSendOptions.Sequenced);
await client.SendAndFlushAsync(new ChatMessage { Text = "fireaway" }, RudpSendOptions.Unreliable);

client.Disconnect(); // -> Remote on the server (peer close notification), not a 5 s timeout

listener.Stop();     // sends disconnect messages to connected peers (NetManager.Stop(true))

// --- app contract: message, converter, handler (same shape as TCP) ---
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

For a manually verifiable sample, run [`Sandbox/Chat.RUDP`](Sandbox/Chat.RUDP/): `server [port]` / `client [port] [name]` (`'!'`-prefixed chat lines go out as `Unreliable`), `--selftest` (all five methods round-tripped, exit 0), `--tls-selftest` (pinning + all methods + chunking over DTLS), and `--bench [--tls]` (loopback echo benchmark).

## See also

- [TCP.md](TCP.md) — the stream stack
- [Document/02-Architecture/Overview.md](Document/02-Architecture/Overview.md) · [Channel](Document/02-Architecture/Channel.md) · [Pipeline](Document/02-Architecture/Pipeline.md)
- [Document/05-Decisions/0007-rudp-three-way-split-and-polling.md](Document/05-Decisions/0007-rudp-three-way-split-and-polling.md) · [0005](Document/05-Decisions/0005-rudp-litenetlib-interim.md) · [0009](Document/05-Decisions/0009-rudp-tls-dtls.md)
