using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Communication.Network.RUDP;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;

// 사용:
//   Chat.RUDP server [port]          — 채팅 서버 (기본 32000)
//   Chat.RUDP client [port] [이름]   — 채팅 클라이언트 (기본 32000 / guest)
//   Chat.RUDP --selftest             — 프로세스 내 서버+클라이언트 자동 검증, 성공 시 exit 0
//   Chat.RUDP --tls-selftest         — TLS(DTLS) 켠 동일 검증(핀닝 연결, 청킹 메시지 포함)
//   Chat.RUDP --bench [--tls] [크기] [횟수]  — 루프백 에코 벤치마크(기본 512바이트 × 5000)
//
// 채팅에서 '!'로 시작하는 줄은 Unreliable로 보내고, 나머지는 ReliableOrdered로 보낸다 —
// 메시지별 전송 옵션(RudpSendOptions)이 실제로 다르게 적용되는지 수동으로 확인할 수 있다.

if (args.Length > 0 && args[0] == "--selftest")
{
    return await RunSelfTestAsync();
}

if (args.Length > 0 && args[0] == "--tls-selftest")
{
    return await RunTlsSelfTestAsync();
}

if (args.Length > 0 && args[0] == "--bench")
{
    return RunBenchmark(Array.AsReadOnly(args));
}

int port = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 32000;
string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "client";

if (mode == "server")
{
    return await RunServerAsync(port);
}

string name = args.Length > 2 ? args[2] : "guest";
return await RunClientAsync(port, name);

static async Task<int> RunSelfTestAsync()
{
    RudpDeliveryMethod[] methods =
    {
        RudpDeliveryMethod.ReliableOrdered,
        RudpDeliveryMethod.ReliableUnordered,
        RudpDeliveryMethod.Sequenced,
        RudpDeliveryMethod.ReliableSequenced,
        RudpDeliveryMethod.Unreliable,
    };

    ConcurrentQueue<string> received = new();
    List<RudpSession> serverSessions = new();
    using RudpListener listener = new(IPAddress.Loopback, 0);
    listener.Accepted += channel =>
    {
        RudpSession session = new(channel, new JsonChatConverter(), s => new SelfTestHandler(s, received));
        lock (serverSessions)
        {
            serverSessions.Add(session);
        }

        Console.WriteLine($"selftest: peer accepted ({listener.ActiveConnectionCount} active)");
    };
    listener.Start();
    int serverPort = listener.LocalPort;

    RudpConnector connector = new();
    if (!await connector.ConnectAsync("127.0.0.1", serverPort))
    {
        Console.Error.WriteLine("selftest: connect failed");
        return 1;
    }

    using RudpSession clientSession = new(connector.Channel!, new JsonChatConverter(), s => new SelfTestHandler(s, received));

    // 전송 방식별로 메시지 1개씩 — 순서 유실 경쟁을 피하려고 도착을 확인하고 다음을 보낸다.
    for (int i = 0; i < methods.Length; i++)
    {
        string text = $"msg-{methods[i]}";
        await clientSession.SendAndFlushAsync(new ChatMessage { Sender = "selftest", Text = text }, new RudpSendOptions(methods[i]));

        int expected = i + 1;
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (received.Count < expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        if (received.Count < expected)
        {
            Console.Error.WriteLine($"selftest: {text} not received ({methods[i]})");
            return 1;
        }

        Console.WriteLine($"selftest: {methods[i]} round-trip OK");
    }

    if (!clientSession.IsConnected())
    {
        Console.Error.WriteLine("selftest: session dropped during round-trips");
        return 1;
    }

    clientSession.Disconnect();
    lock (serverSessions)
    {
        foreach (RudpSession session in serverSessions)
        {
            session.Dispose();
        }
    }

    Console.WriteLine($"selftest OK — {methods.Length}/{methods.Length} messages round-tripped");
    return 0;
}

static async Task<int> RunTlsSelfTestAsync()
{
    using X509Certificate2 certificate = CreateDemoCertificate();
    byte[] certificateDer = certificate.GetRawCertData();
    string fingerprint = RudpTlsOptions.GetSha256Fingerprint(certificateDer);
    Console.WriteLine($"tls-selftest: 서버 인증서 지문 = {fingerprint}");

    // 채널 단위 에코 서버 — 세션·컨버터 없이 TLS 경로만 검증한다(핀닝 연결·전 방식 왕복·청킹).
    using RudpListener listener = new(IPAddress.Loopback, 0);
    listener.Accepted += channel =>
    {
        channel.MessageReceived += payload =>
        {
            _ = channel.SendAsync(payload.ToArray());
        };
    };
    listener.Start(new RudpTransportOptions { Tls = new RudpTlsOptions { ServerCertificate = certificate } });

    // 클라이언트는 지문으로 핀닝한다 — 서버 인증서 DER의 SHA-256을 비교(공개망 표준 경로).
    RudpConnector connector = new();
    if (!await connector.ConnectAsync("127.0.0.1", listener.LocalPort, new RudpTransportOptions
    {
        Tls = new RudpTlsOptions { RemoteCertificateValidation = der => RudpTlsOptions.GetSha256Fingerprint(der) == fingerprint },
    }))
    {
        Console.Error.WriteLine("tls-selftest: 연결 실패");
        return 1;
    }

    using IDisposable channelLease = connector.Channel!;
    Console.WriteLine("tls-selftest: 핀닝 연결 확립");

    RudpDeliveryMethod[] methods =
    {
        RudpDeliveryMethod.ReliableOrdered,
        RudpDeliveryMethod.ReliableUnordered,
        RudpDeliveryMethod.Sequenced,
        RudpDeliveryMethod.ReliableSequenced,
        RudpDeliveryMethod.Unreliable,
    };

    int received = 0;
    connector.Channel.MessageReceived += _ => Interlocked.Increment(ref received);

    // 방식별로 도착을 확인하고 다음을 보낸다 — 순서 유실 경쟁 회피(plain selftest와 동일 패턴).
    for (int i = 0; i < methods.Length; i++)
    {
        byte[] message = new byte[64 + i];
        new Random(11 + i).NextBytes(message);
        await connector.Channel.SendAsync(message, new RudpSendOptions(methods[i]));

        int expected = i + 1;
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref received) < expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        if (Volatile.Read(ref received) < expected)
        {
            Console.Error.WriteLine($"tls-selftest: {methods[i]} 왕복 실패");
            return 1;
        }

        Console.WriteLine($"tls-selftest: {methods[i]} 왕복 OK");
    }

    // 청킹 — TLS 레코드 상한(16,384)을 넘는 메시지가 봉투 재조립으로 왕복한다.
    byte[] big = new byte[200_000];
    new Random(99).NextBytes(big);
    int bigEchoed = 0;
    connector.Channel.MessageReceived += payload =>
    {
        if (payload.Length == big.Length)
        {
            Interlocked.Increment(ref bigEchoed);
        }
    };

    await connector.Channel.SendAsync(big);
    DateTime bigDeadline = DateTime.UtcNow.AddSeconds(20);
    while (Volatile.Read(ref bigEchoed) == 0 && DateTime.UtcNow < bigDeadline)
    {
        await Task.Delay(20);
    }

    if (Volatile.Read(ref bigEchoed) == 0)
    {
        Console.Error.WriteLine("tls-selftest: 청킹 메시지(200KB) 왕복 실패");
        return 1;
    }

    connector.Channel.Dispose();
    Console.WriteLine("tls-selftest OK — 핀닝 연결·전 방식 왕복·청킹 검증 통과");
    return 0;
}

static int RunBenchmark(IReadOnlyList<string> args)
{
    bool tls = args.Count > 1 && args[1] == "--tls";
    int index = tls ? 2 : 1;
    int size = args.Count > index && int.TryParse(args[index], out int parsedSize) ? parsedSize : 512;
    int count = args.Count > index + 1 && int.TryParse(args[index + 1], out int parsedCount) ? parsedCount : 5_000;
    return RunBenchmarkAsync(tls, size, count).GetAwaiter().GetResult();
}

static async Task<int> RunBenchmarkAsync(bool tls, int size, int count)
{
    // 성능 게이트 도구 — 평문 vs TLS(DTLS) 루프백 에코 처리량 비교. BouncyCastle 관리형 암호의
    // 실측치를 여기서 확인하고, 부족하면 그때 대안을 검토한다(Rudp-Tls-Feasibility 참조).
    X509Certificate2? certificate = null;
    RudpTransportOptions serverOptions = new();
    if (tls)
    {
        certificate = CreateDemoCertificate();
        serverOptions.Tls = new RudpTlsOptions { ServerCertificate = certificate };
    }

    using RudpListener listener = new(IPAddress.Loopback, 0);
    listener.Accepted += channel =>
    {
        channel.MessageReceived += payload =>
        {
            _ = channel.SendAsync(payload.ToArray());
        };
    };
    listener.Start(serverOptions);

    RudpTransportOptions clientOptions = new();
    if (tls)
    {
        byte[] der = certificate!.GetRawCertData();
        clientOptions.Tls = new RudpTlsOptions { RemoteCertificateValidation = received => received.AsSpan().SequenceEqual(der) };
    }

    RudpConnector connector = new();
    if (!await connector.ConnectAsync("127.0.0.1", listener.LocalPort, clientOptions))
    {
        Console.Error.WriteLine("bench: 연결 실패");
        return 1;
    }

    using IDisposable channelLease = connector.Channel!;
    byte[] message = new byte[size];
    new Random(3).NextBytes(message);

    long echoed = 0;
    connector.Channel.MessageReceived += _ => Interlocked.Increment(ref echoed);

    Console.WriteLine($"bench[{(tls ? "tls" : "plain")}] {count:N0} × {size:N0}B 에코 왕복 시작…");
    Stopwatch watch = Stopwatch.StartNew();
    for (int i = 0; i < count; i++)
    {
        await connector.Channel.SendAsync(message);
    }

    while (Volatile.Read(ref echoed) < count && watch.Elapsed.TotalSeconds < 60)
    {
        await Task.Delay(10);
    }

    watch.Stop();
    connector.Channel.Dispose();

    double seconds = watch.Elapsed.TotalSeconds;
    Console.WriteLine(
        $"bench[{(tls ? "tls" : "plain")}] 에코 {Volatile.Read(ref echoed):N0}/{count:N0}, " +
        $"{count / seconds:F0} msg/s, {count * (double)size / 1024 / 1024 / seconds:F2} MB/s");

    return Volatile.Read(ref echoed) == count ? 0 : 1;
}

/// <summary>데모용 자체 서명 인증서 — 실서비스는 사내 CA 발급 또는 고정 키로 교체한다.</summary>
static X509Certificate2 CreateDemoCertificate()
{
    using RSA rsa = RSA.Create(2048);
    CertificateRequest request = new("CN=ds-chat-demo", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using X509Certificate2 ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
    return new X509Certificate2(
        ephemeral.Export(X509ContentType.Pfx),
        (string?)null,
        X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
}

static async Task<int> RunServerAsync(int port)
{
    using RudpListener listener = new(IPAddress.Any, port);

    List<RudpSession> sessions = new();
    listener.Accepted += channel =>
    {
        RudpSession session = new(channel, new JsonChatConverter(), s => new ChatHandler(s, isServer: true));
        ChatHandler.Join(session);
        lock (sessions)
        {
            sessions.Add(session);
        }

        session.Disconnected += (_, e) =>
        {
            ChatHandler.Leave(session);
            lock (sessions)
            {
                // 리스트는 살아있는 세션 추적용 — 끊긴 세션을 제거하지 않으면
                // 장기 구동 서버가 종료 접속 세션(+호스트 그래프)을 무제한 보유한다.
                sessions.Remove(session);
            }

            Console.WriteLine($"client left ({e.Reason}) — total {ChatHandler.RoomCount}");
        };

        Console.WriteLine($"client accepted — total {ChatHandler.RoomCount}");
    };

    listener.Start();
    Console.WriteLine($"RUDP server listening on {listener.LocalPort} — Ctrl+C to stop");
    await Task.Delay(Timeout.Infinite);
    return 0;
}

static async Task<int> RunClientAsync(int port, string name)
{
    RudpConnector connector = new();
    if (!await connector.ConnectAsync("127.0.0.1", port))
    {
        Console.WriteLine($"connect failed (127.0.0.1:{port})");
        return 1;
    }

    RudpSession session = new(connector.Channel!, new JsonChatConverter(), s => new ChatHandler(s, isServer: false));
    session.Disconnected += (_, e) => Console.WriteLine($"disconnected: {e.Reason}");
    Console.WriteLine($"connected as {name}. type messages ('!' prefix = Unreliable), 'exit' to quit.");

    while (session.IsConnected())
    {
        string? line = Console.ReadLine();
        if (line is null || line == "exit")
        {
            break;
        }

        bool unreliable = line.StartsWith('!');
        string text = unreliable ? line[1..] : line;
        RudpSendOptions options = unreliable ? RudpSendOptions.Unreliable : RudpSendOptions.ReliableOrdered;
        await session.SendAndFlushAsync(new ChatMessage { Sender = name, Text = text }, options);
    }

    session.Disconnect();
    return 0;
}

/// <summary>샘플용 메시지. 직렬화 포맷은 앱 책임(라이브러리 범위 밖).</summary>
public sealed class ChatMessage
{
    public string Sender { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>샘플 로컬 직렬화기 — JSON. DS_MessageProtocol 주입 자리.</summary>
public sealed class JsonChatConverter : IMessageConverter
{
    public void Serialize(object message, IBufferWriter<byte> writer)
    {
        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType());
        writer.Write(utf8);
    }

    public object Deserialize(ReadOnlySpan<byte> message)
        => JsonSerializer.Deserialize<ChatMessage>(message) ?? new ChatMessage();
}

/// <summary>서버는 수신 메시지를 다른 모두에게 방송하고, 클라이언트는 출력만 한다.</summary>
public sealed class ChatHandler : MessageHandler
{
    private static readonly ConcurrentDictionary<ISession, byte> Room = new();
    private readonly bool _isServer;

    public ChatHandler(ISession session, bool isServer)
        : base(session)
    {
        _isServer = isServer;
        Register<ChatMessage>(OnChat);
    }

    public static int RoomCount => Room.Count;

    public static void Join(ISession session) => Room[session] = 0;

    public static void Leave(ISession session) => Room.TryRemove(session, out _);

    private static void Broadcast(ChatMessage message, ISession? except)
    {
        foreach (ISession peer in Room.Keys)
        {
            if (!ReferenceEquals(peer, except) && peer.IsConnected())
            {
                _ = peer.SendAsync(message, RudpSendOptions.ReliableOrdered);
            }
        }
    }

    private void OnChat(ChatMessage message)
    {
        Console.WriteLine($"[{message.Sender}] {message.Text}");

        if (_isServer)
        {
            Broadcast(message, Session); // 송신자에게는 에코하지 않는다.
        }
    }
}

/// <summary>selftest용 — 수신 텍스트를 큐에 기록만 한다.</summary>
public sealed class SelfTestHandler : MessageHandler
{
    private readonly ConcurrentQueue<string> _received;

    public SelfTestHandler(ISession session, ConcurrentQueue<string> received)
        : base(session)
    {
        _received = received;
        Register<ChatMessage>(OnChat);
    }

    private void OnChat(ChatMessage message) => _received.Enqueue(message.Text);
}
