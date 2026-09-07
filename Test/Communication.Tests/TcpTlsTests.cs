using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Communication.Network.TCP;
using Communication.Shared.Channels;
using Communication.Shared.Connection;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using Xunit;
using RawTcpClient = System.Net.Sockets.TcpClient;
using RawTcpListener = System.Net.Sockets.TcpListener;
using TcpListener = Communication.Network.TCP.TcpListener;

namespace Communication.Tests;

/// <summary>
/// TCP TLS(SslStream) 경로 — 핸드셰이크 성공·실패·상한, 슬롯 회수, 옵션 검증.
/// 실 소켓·타이밍 마감 사용 — 다른 네트워크 클래스와 한 컬렉션에서 순차 실행(결정적 테스트).
/// </summary>
[Collection("network-loopback")]
public class TcpTlsTests
{
    private static X509Certificate2 CreateTestCertificate()
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new("CN=ds-communication-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Linux(OpenSSL)의 SslStream 서버 인증은 serverAuth EKU를 요구한다.
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, critical: false));

        using X509Certificate2 ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(30));

        // Windows Schannel은 ephemeral 개인키로 서버 인증을 못 한다("플랫폼이 ephemeral 키를 지원하지 않음") —
        // PFX로 재수입해 키를 사용자 키 저장소에 지속시킨다(Linux에서는 무의미하지만 무해).
        X509Certificate2 certificate = new(
            ephemeral.Export(X509ContentType.Pfx),
            (string?)null,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
        return certificate;
    }

    private static TcpTransportOptions ServerOptions(X509Certificate certificate, int handshakeTimeoutMs = TcpTlsOptions.DefaultHandshakeTimeoutMs)
        => new() { Tls = new TcpTlsOptions { ServerCertificate = certificate, HandshakeTimeout = handshakeTimeoutMs } };

    private static TcpTransportOptions ClientOptions(int handshakeTimeoutMs = TcpTlsOptions.DefaultHandshakeTimeoutMs)
        => new()
        {
            Tls = new TcpTlsOptions
            {
                RemoteCertificateValidation = (_, _, _, _) => true,
                HandshakeTimeout = handshakeTimeoutMs,
            },
        };

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("조건이 시간 안에 만족되지 않았습니다.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class EchoHandler : MessageHandler
    {
        private readonly List<object> _received = new();

        public EchoHandler(ISession session)
            : base(session)
        {
            Register<string>(OnMessage);
        }

        public string[] Snapshot()
        {
            lock (_received)
            {
                return _received.Cast<string>().ToArray();
            }
        }

        private void OnMessage(string message)
        {
            lock (_received)
            {
                _received.Add(message);
            }

            if (message == "ping")
            {
                _ = Session.SendAsync("pong");
            }
        }
    }

    [Fact]
    public async Task Tls_Connect_EchoRoundtrip_DisconnectPropagates()
    {
        using X509Certificate2 certificate = CreateTestCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        TcpSession? serverSession = null;
        EchoHandler? serverHandler = null;
        var serverDisconnected = new TaskCompletionSource<DisconnectReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.Accepted += channel =>
        {
            serverSession = new TcpSession(channel, new StringConverter(), session =>
            {
                EchoHandler handler = new(session);
                serverHandler = handler;
                session.Disconnected += (_, e) => serverDisconnected.TrySetResult(e.Reason);
                return handler;
            });
        };
        listener.Start(ServerOptions(certificate));
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

        var connector = new TcpConnector();
        bool connected = await connector.ConnectAsync("127.0.0.1", port, ClientOptions());
        Assert.True(connected);
        Assert.NotNull(connector.Channel);

        EchoHandler? clientHandler = null;
        using TcpSession clientSession = new(connector.Channel!, new StringConverter(), s =>
        {
            clientHandler = new EchoHandler(s);
            return clientHandler;
        });

        await clientSession.SendAsync("ping");
        await WaitUntilAsync(() => serverHandler is { } s && s.Snapshot().Length > 0 && clientHandler is { } c && c.Snapshot().Length > 0);
        Assert.Equal(new[] { "ping" }, serverHandler!.Snapshot());
        Assert.Equal(new[] { "pong" }, clientHandler!.Snapshot());

        clientSession.Dispose(); // 로컬 끊김 → TLS 연결 닫힘 → 서버는 Remote로 관측
        Assert.Equal(DisconnectReason.Remote, await serverDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(serverSession!.IsConnected());
    }

    [Fact]
    public async Task Tls_DefaultValidation_RejectsSelfSignedCertificate()
    {
        using X509Certificate2 certificate = CreateTestCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Accepted += channel => channel.Dispose(); // 수락된 채널의 소유자 — 즉시 정리해 슬롯을 회수한다.
        listener.Start(ServerOptions(certificate));
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

        // 검증 콜백 없음(기본 OS 검증) — 신뢰 체인에 없는 자체 서명 인증서는 거부된다.
        var connector = new TcpConnector();
        bool connected = await connector.ConnectAsync("127.0.0.1", port, new TcpTransportOptions { Tls = new TcpTlsOptions() });
        Assert.False(connected);
        Assert.Null(connector.Channel);
        await WaitUntilAsync(() => listener.ActiveConnectionCount == 0); // 서버 쪽 슬롯도 정리된다.
    }

    [Fact]
    public async Task Tls_Server_RejectsPlaintextClient_AndReleasesSlot()
    {
        using X509Certificate2 certificate = CreateTestCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Accepted += channel => channel.Dispose(); // 수락된 채널의 소유자 — 즉시 정리해 슬롯을 회수한다.
        listener.Start(ServerOptions(certificate));
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

        // 평문(비-TLS) 클라이언트 — TLS 레코드 헤더(5바이트) 이상의 평문 바이트를 보내 핸드셰이크를 위반한다.
        using RawTcpClient raw = new();
        await raw.ConnectAsync(IPAddress.Loopback, port);
        await raw.GetStream().WriteAsync("GET / HTTP/1.1\r\n"u8.ToArray());

        await WaitUntilAsync(() => listener.ActiveConnectionCount == 0);
        Assert.Equal(0, listener.ActiveConnectionCount);
    }

    [Fact]
    public async Task Tls_SilentClient_HandshakeTimeout_ReleasesSlot()
    {
        using X509Certificate2 certificate = CreateTestCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Accepted += channel => channel.Dispose(); // 수락된 채널의 소유자 — 즉시 정리해 슬롯을 회수한다.
        listener.Start(ServerOptions(certificate, handshakeTimeoutMs: 300));
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

        // 연결만 열고 ClientHello를 보내지 않는 클라이언트 — 슬로로리스 핸드셰이크.
        using RawTcpClient raw = new();
        await raw.ConnectAsync(IPAddress.Loopback, port);

        await WaitUntilAsync(() => listener.ActiveConnectionCount == 0);
        Assert.Equal(0, listener.ActiveConnectionCount);
    }

    [Fact]
    public async Task Tls_ClientHandshakeTimeout_AgainstSilentServer_ReturnsFalse()
    {
        // 응답하지 않는 서버(OS 백로그만 채우고 앱이 읽지 않는다) — 클라이언트 핸드셰이크 상한 경로.
        using RawTcpListener rawListener = new(IPAddress.Loopback, 0);
        rawListener.Start();
        int port = ((IPEndPoint)rawListener.LocalEndpoint).Port;

        var connector = new TcpConnector();
        bool connected = await connector.ConnectAsync("127.0.0.1", port, ClientOptions(handshakeTimeoutMs: 300));
        Assert.False(connected);
        Assert.Null(connector.Channel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TlsOptions_HandshakeTimeout_NonPositive_Throws(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new TcpTlsOptions { HandshakeTimeout = value });

    [Fact]
    public void TlsOptions_EmptyTargetHost_Throws()
        => Assert.Throws<ArgumentException>(() => new TcpTlsOptions { TargetHost = "" });
}
