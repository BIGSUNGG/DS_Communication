using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Communication.Network.RUDP;
using Communication.Shared.Channels;
using Xunit;

namespace Communication.Tests;

/// <summary>
/// RUDP TLS(DTLS 1.2) 경로 — 핀닝 성공·거부, TargetHost 일치·불일치, 기본 거부(fail-closed),
/// 핸드셰이크 상한·중 끊김 슬롯 회수, 전송 방식 혼합, 큰 payload 분할.
/// 실 소켓·타이밍 마감 사용 — 다른 네트워크 클래스와 한 컬렉션에서 순차 실행(결정적 테스트).
/// </summary>
[Collection("network-loopback")]
public class RudpTlsTests
{
    private static X509Certificate2 CreateRsaTestCertificate()
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new("CN=ds-communication-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, critical: false));

        using X509Certificate2 ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(30));

        // 키 소유 경로(BC PKCS8 내보내기)가 플랫폼 키 저장소와 무관하게 동작하도록 PFX로 재수입한다(TcpTlsTests와 동일 패턴).
        return new X509Certificate2(
            ephemeral.Export(X509ContentType.Pfx),
            (string?)null,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
    }

    private static X509Certificate2 CreateEcdsaTestCertificate()
    {
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=ds-communication-test", ecdsa, HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, critical: false));

        using X509Certificate2 ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(30));

        return new X509Certificate2(
            ephemeral.Export(X509ContentType.Pfx),
            (string?)null,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
    }

    private static RudpTransportOptions ServerOptions(X509Certificate2 certificate, int handshakeTimeoutMs = RudpTlsOptions.DefaultHandshakeTimeoutMs)
        => new() { Tls = new RudpTlsOptions { ServerCertificate = certificate, HandshakeTimeout = handshakeTimeoutMs } };

    private static RudpTransportOptions PinnedClientOptions(byte[] expectedDer, int handshakeTimeoutMs = RudpTlsOptions.DefaultHandshakeTimeoutMs)
        => new()
        {
            Tls = new RudpTlsOptions
            {
                RemoteCertificateValidation = der => der.AsSpan().SequenceEqual(expectedDer),
                HandshakeTimeout = handshakeTimeoutMs,
            },
        };

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 15000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    private static byte[] Payload(int size)
    {
        byte[] payload = new byte[size];
        new Random(42).NextBytes(payload);
        return payload;
    }

    /// <summary>에코 서버 — 수신 payload를 그대로 되돌린다(채널 단위 테스트 — 세션·컨버터 불필요).</summary>
    private static RudpListener StartEchoListener(RudpTransportOptions options, ConcurrentQueue<byte[]>? serverReceived = null)
    {
        RudpListener listener = new(IPAddress.Loopback, 0);
        listener.Accepted += channel =>
        {
            channel.MessageReceived += payload =>
            {
                serverReceived?.Enqueue(payload.ToArray());
                _ = channel.SendAsync(payload.ToArray()); // 콜백 밖 유효 계약 — 복사해 에코한다.
            };
        };
        listener.Start(options);
        return listener;
    }

    private static async Task<RudpConnector> ConnectPinnedClientAsync(int port, byte[] serverCertificateDer)
    {
        RudpConnector connector = new();
        Assert.True(await connector.ConnectAsync("127.0.0.1", port, PinnedClientOptions(serverCertificateDer)));
        return connector;
    }

    [Fact]
    public async Task PinnedConnection_RsaCertificate_EchoRoundTrip()
    {
        using X509Certificate2 certificate = CreateRsaTestCertificate();
        using RudpListener listener = StartEchoListener(ServerOptions(certificate));

        RudpConnector connector = await ConnectPinnedClientAsync(listener.LocalPort, certificate.GetRawCertData());
        using IDisposable _ = connector.Channel!;

        ConcurrentQueue<byte[]> received = new();
        connector.Channel!.MessageReceived += payload => received.Enqueue(payload.ToArray());

        byte[] message = Payload(256);
        await connector.Channel.SendAsync(message);

        await WaitUntilAsync(() => received.Count >= 1);
        Assert.Single(received);
        Assert.Equal(message, received.Single());
    }

    [Fact]
    public async Task PinnedConnection_EcdsaCertificate_EchoRoundTrip()
    {
        using X509Certificate2 certificate = CreateEcdsaTestCertificate();
        using RudpListener listener = StartEchoListener(ServerOptions(certificate));

        RudpConnector connector = await ConnectPinnedClientAsync(listener.LocalPort, certificate.GetRawCertData());
        using IDisposable _ = connector.Channel!;

        ConcurrentQueue<byte[]> received = new();
        connector.Channel!.MessageReceived += payload => received.Enqueue(payload.ToArray());

        byte[] message = Payload(128);
        await connector.Channel.SendAsync(message);

        await WaitUntilAsync(() => received.Count >= 1);
        Assert.Single(received);
        Assert.Equal(message, received.Single());
    }

    [Fact]
    public async Task TargetHost_MatchesCertificateCn_Connects()
    {
        using X509Certificate2 certificate = CreateRsaTestCertificate();
        using RudpListener listener = StartEchoListener(ServerOptions(certificate));

        RudpConnector connector = new();
        Assert.True(await connector.ConnectAsync("127.0.0.1", listener.LocalPort, new RudpTransportOptions
        {
            Tls = new RudpTlsOptions { TargetHost = "ds-communication-test" },
        }));
        using IDisposable _ = connector.Channel!;

        ConcurrentQueue<byte[]> received = new();
        connector.Channel!.MessageReceived += payload => received.Enqueue(payload.ToArray());
        await connector.Channel.SendAsync(Payload(64));
        await WaitUntilAsync(() => received.Count >= 1);
        Assert.Single(received);
    }

    [Fact]
    public async Task TargetHost_Mismatch_RejectsConnection()
    {
        using X509Certificate2 certificate = CreateRsaTestCertificate();
        using RudpListener listener = StartEchoListener(ServerOptions(certificate));

        RudpConnector connector = new();
        Assert.False(await connector.ConnectAsync("127.0.0.1", listener.LocalPort, new RudpTransportOptions
        {
            Tls = new RudpTlsOptions { TargetHost = "other-host" },
        }));

        await WaitUntilAsync(() => listener.ActiveConnectionCount == 0); // 서버 슬롯 회수
        Assert.Equal(0, listener.ActiveConnectionCount);
    }

    [Fact]
    public async Task PinningMismatch_RejectsConnectionAndRecoversSlot()
    {
        using X509Certificate2 certificate = CreateRsaTestCertificate();
        using RudpListener listener = StartEchoListener(ServerOptions(certificate));

        RudpConnector connector = new();
        Assert.False(await connector.ConnectAsync("127.0.0.1", listener.LocalPort, new RudpTransportOptions
        {
            Tls = new RudpTlsOptions { RemoteCertificateValidation = _ => false },
        }));

        await WaitUntilAsync(() => listener.ActiveConnectionCount == 0);
        Assert.Equal(0, listener.ActiveConnectionCount);
    }

    [Fact]
    public async Task NoValidationConfigured_RejectsByDefault()
    {
        using X509Certificate2 certificate = CreateRsaTestCertificate();
        using RudpListener listener = StartEchoListener(ServerOptions(certificate));

        RudpConnector connector = new();
        Assert.False(await connector.ConnectAsync("127.0.0.1", listener.LocalPort, new RudpTransportOptions
        {
            Tls = new RudpTlsOptions(), // 검증 수단 없음 — 기본 거부(fail-closed)
        }));
    }

    [Fact]
    public async Task HandshakeTimeout_PlaintextPeer_SlotRecovered()
    {
        using X509Certificate2 certificate = CreateRsaTestCertificate();
        using RudpListener listener = StartEchoListener(ServerOptions(certificate, handshakeTimeoutMs: 1000));

        // 평문 클라이언트 — LiteNetLib 연결(키 수락)은 되지만 DTLS ClientHello를 보내지 않는다.
        RudpConnector plainClient = new();
        Assert.True(await plainClient.ConnectAsync("127.0.0.1", listener.LocalPort));
        using IDisposable _ = plainClient.Channel!;

        await WaitUntilAsync(() => listener.ActiveConnectionCount == 0, timeoutMs: 10_000); // 상한 1초 + 여유
        Assert.Equal(0, listener.ActiveConnectionCount); // 핸드셰이크 상한 초과 → 폐기·슬롯 회수
    }

    [Fact]
    public async Task DisconnectDuringHandshake_SlotRecovered()
    {
        using X509Certificate2 certificate = CreateRsaTestCertificate();
        using RudpListener listener = StartEchoListener(ServerOptions(certificate));

        RudpConnector plainClient = new();
        Assert.True(await plainClient.ConnectAsync("127.0.0.1", listener.LocalPort));
        plainClient.Channel!.Dispose(); // 핸드셰이크 진행 중 즉시 끊기 — 상한보다 먼저

        await WaitUntilAsync(() => listener.ActiveConnectionCount == 0);
        Assert.Equal(0, listener.ActiveConnectionCount);
    }

    [Fact]
    public async Task MixedDeliveryMethods_AllRoundTripThroughTls()
    {
        using X509Certificate2 certificate = CreateRsaTestCertificate();
        using RudpListener listener = StartEchoListener(ServerOptions(certificate));

        RudpConnector connector = await ConnectPinnedClientAsync(listener.LocalPort, certificate.GetRawCertData());
        using IDisposable _ = connector.Channel!;

        ConcurrentQueue<byte[]> received = new();
        connector.Channel!.MessageReceived += payload => received.Enqueue(payload.ToArray());

        RudpDeliveryMethod[] methods =
        {
            RudpDeliveryMethod.ReliableOrdered,
            RudpDeliveryMethod.ReliableUnordered,
            RudpDeliveryMethod.Sequenced,
            RudpDeliveryMethod.ReliableSequenced,
            RudpDeliveryMethod.Unreliable,
        };

        // 방식별로 도착을 확인하고 다음을 보낸다 — 순서 유실 경쟁 회피(RudpLoopbackTests와 동일 패턴).
        for (int i = 0; i < methods.Length; i++)
        {
            byte[] message = Payload(32 + i);
            await connector.Channel.SendAsync(message, new RudpSendOptions(methods[i]));

            int expected = i + 1;
            await WaitUntilAsync(() => received.Count >= expected);
            Assert.Equal(expected, received.Count);
        }
    }

    [Fact]
    public async Task LargeReliablePayload_FragmentsThroughTlsRecords()
    {
        using X509Certificate2 certificate = CreateRsaTestCertificate();
        using RudpListener listener = StartEchoListener(ServerOptions(certificate));

        RudpConnector connector = await ConnectPinnedClientAsync(listener.LocalPort, certificate.GetRawCertData());
        using IDisposable _ = connector.Channel!;

        ConcurrentQueue<byte[]> received = new();
        connector.Channel!.MessageReceived += payload => received.Enqueue(payload.ToArray());

        byte[] message = Payload(200_000); // TLS 레코드 상한(16,384) 초과 — 채널 봉투가 청킹·재조립한다
        await connector.Channel.SendAsync(message);

        await WaitUntilAsync(() => received.Any(p => p.Length == message.Length), timeoutMs: 20_000);
        Assert.Contains(received, p => p.AsSpan().SequenceEqual(message));
    }

    [Fact]
    public async Task OversizePayload_NonFragmentableMethod_Rejected()
    {
        using X509Certificate2 certificate = CreateRsaTestCertificate();
        using RudpListener listener = StartEchoListener(ServerOptions(certificate));

        RudpConnector connector = await ConnectPinnedClientAsync(listener.LocalPort, certificate.GetRawCertData());
        using IDisposable _ = connector.Channel!;

        // 청킹은 ReliableOrdered만 가능 — 비신뢰 방식의 초과 payload는 조용한 유실 대신 즉시 실패한다.
        ArgumentException? error = await Assert.ThrowsAsync<ArgumentException>(
            () => connector.Channel.SendAsync(Payload(66_000), RudpSendOptions.Unreliable).AsTask());
        Assert.Contains("청킹", error.Message);
    }
}
