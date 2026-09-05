using System.Net;
using Communication.Network.TCP;
using TcpClient = System.Net.Sockets.TcpClient;
using Communication.Shared.Channels;
using Communication.Shared.Connection;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using Xunit;

namespace Communication.Tests;

public class TcpLoopbackTests
{
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

        public int ReceivedCount
        {
            get
            {
                lock (_received)
                {
                    return _received.Count;
                }
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
                _ = Session.SendAsync("pong"); // 에코
            }
        }
    }

    [Fact]
    public async Task Connect_SendEcho_Disconnect_RaisesReasons()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        TcpSession? serverSession = null;
        EchoHandler? serverHandler = null;
        listener.Accepted += channel =>
            serverSession = new TcpSession(channel, new StringConverter(), session =>
            {
                serverHandler = new EchoHandler(session);
                return serverHandler;
            });
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

        var connector = new TcpConnector();
        bool connected = await connector.ConnectAsync("127.0.0.1", port);
        Assert.True(connected);

        EchoHandler? clientHandler = null;
        using var clientSession = new TcpSession(connector.Channel!, new StringConverter(), session =>
        {
            clientHandler = new EchoHandler(session);
            return clientHandler;
        });

        await clientSession.SendAndFlushAsync("ping");

        await WaitUntilAsync(() => serverHandler?.ReceivedCount == 1); // 서버 수신
        await WaitUntilAsync(() => clientHandler?.ReceivedCount == 1); // 에코 왕복

        DisconnectReason? clientReason = null;
        DisconnectReason? serverReason = null;
        clientSession.Disconnected += (_, e) => clientReason = e.Reason;
        serverSession!.Disconnected += (_, e) => serverReason = e.Reason;

        clientSession.Disconnect();

        await WaitUntilAsync(() => clientReason != null && serverReason != null);
        Assert.Equal(DisconnectReason.Local, clientReason);
        Assert.Equal(DisconnectReason.Remote, serverReason);
    }

    [Fact]
    public async Task Connect_WithKeepAliveOptions_RoundTrips()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        EchoHandler? serverHandler = null;
        listener.Accepted += channel =>
            _ = new TcpSession(channel, new StringConverter(), session =>
            {
                serverHandler = new EchoHandler(session);
                return serverHandler;
            });
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

        var connector = new TcpConnector();
        var options = new TcpTransportOptions
        {
            KeepAlive = new SocketKeepAliveOptions
            {
                Enabled = true,
                IdleTime = TimeSpan.FromSeconds(30),
                Interval = TimeSpan.FromSeconds(5),
            },
        };

        bool connected = await connector.ConnectAsync("127.0.0.1", port, options);
        Assert.True(connected);

        using var clientSession = new TcpSession(connector.Channel!, new StringConverter(), session => new EchoHandler(session));
        await clientSession.SendAndFlushAsync("keep-alive-check");

        await WaitUntilAsync(() => serverHandler?.ReceivedCount == 1);
    }

    [Fact]
    public async Task Connect_NoDelay_DefaultTrue_AndHonorsExplicitFalse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        var accepted = new List<IByteChannel>();
        listener.Accepted += channel =>
        {
            lock (accepted)
            {
                accepted.Add(channel);
            }
        };
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

        // 옵션 미지정 → 기본 NoDelay(true)가 연결·수락 양쪽 소켓에 적용.
        var connector = new TcpConnector();
        Assert.True(await connector.ConnectAsync("127.0.0.1", port));
        await WaitUntilAsync(() => accepted.Count == 1);

        Assert.True(((StreamByteChannel)connector.Channel!).Socket.NoDelay);
        lock (accepted)
        {
            Assert.True(((StreamByteChannel)accepted[0]).Socket.NoDelay);
        }

        // 명시적 false → Nagle 유지(OS 설정).
        var nagleConnector = new TcpConnector();
        Assert.True(await nagleConnector.ConnectAsync("127.0.0.1", port, new TcpTransportOptions { NoDelay = false }));
        Assert.False(((StreamByteChannel)nagleConnector.Channel!).Socket.NoDelay);

        connector.Channel!.Dispose();
        nagleConnector.Channel!.Dispose();
        lock (accepted)
        {
            foreach (IByteChannel channel in accepted)
            {
                channel.Dispose();
            }
        }
    }

    [Fact]
    public async Task SubscribeAfterStart_ReceivesAcceptedChannels()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

        // Start 이후 구독 — 최신 구독자를 읽어야 채널을 받는다.
        IByteChannel? acceptedChannel = null;
        listener.Accepted += channel => acceptedChannel = channel;

        var connector = new TcpConnector();
        Assert.True(await connector.ConnectAsync("127.0.0.1", port));

        await WaitUntilAsync(() => acceptedChannel != null); // 옛 스냅샷 방식이면 여기서 타임아웃.

        connector.Channel!.Dispose();
        acceptedChannel!.Dispose();
    }

    [Fact]
    public async Task Connect_ToClosedPort_ReturnsFalse()
    {
        // 임시 포트 하나를 열었다 닫아 "아무도 안 듣는 포트"를 만든다.
        using (var placeholder = new TcpListener(IPAddress.Loopback, 0))
        {
            placeholder.Start();
            int closedPort = ((IPEndPoint)placeholder.LocalEndpoint!).Port;
            placeholder.Stop();

            var connector = new TcpConnector();
            Assert.False(await connector.ConnectAsync("127.0.0.1", closedPort));
            Assert.Null(connector.Channel);
        }
    }

    [Fact]
    public async Task Connect_Cancelled_ThrowsOperationCanceled()
    {
        var connector = new TcpConnector();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => connector.ConnectAsync("127.0.0.1", 1, cancellationToken: cts.Token));
        Assert.Null(connector.Channel);
    }

    [Fact]
    public async Task MaxConnections_OverLimitConnection_IsRejectedImmediately()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        var accepted = new List<IByteChannel>();
        listener.Accepted += channel =>
        {
            lock (accepted)
            {
                accepted.Add(channel);
            }
        };
        listener.Start(new TcpTransportOptions { MaxConnections = 2 });
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

        // 상한(2)까지는 수락된다.
        using var c1 = new TcpClient();
        using var c2 = new TcpClient();
        await c1.ConnectAsync(IPAddress.Loopback, port);
        await c2.ConnectAsync(IPAddress.Loopback, port);
        await WaitUntilAsync(() =>
        {
            lock (accepted) return accepted.Count == 2;
        });
        Assert.Equal(2, listener.ActiveConnectionCount);

        // 상한 초과 연결 — 수락 직후 서버가 즉시 닫는다(읽기가 0으로 끝남).
        using var c3 = new TcpClient();
        await c3.ConnectAsync(IPAddress.Loopback, port);
        byte[] buffer = new byte[16];
        int read = await c3.GetStream().ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, read); // 서버 측 닫힘 — 연결 거부.
        Assert.Equal(2, listener.ActiveConnectionCount); // 상한은 그대로.
        lock (accepted) Assert.Equal(2, accepted.Count); // 거부된 연결은 Accepted 통지를 받지 않는다.

        // 슬롯 회수 — 채널 Dispose 후엔 다시 수락된다.
        lock (accepted) accepted[0].Dispose();
        await WaitUntilAsync(() => listener.ActiveConnectionCount == 1);

        using var c4 = new TcpClient();
        await c4.ConnectAsync(IPAddress.Loopback, port);
        await WaitUntilAsync(() =>
        {
            lock (accepted) return accepted.Count == 3;
        });
        Assert.Equal(2, listener.ActiveConnectionCount);

        lock (accepted)
        {
            foreach (IByteChannel channel in accepted)
            {
                channel.Dispose();
            }
        }
    }

    /// <summary>
    /// `ConnectTimeout` 상한 — 응답 없는(블랙홀) 호스트에 대한 연결 실패를 OS SYN 재시도 기본
    /// (Windows 약 21초) 대신 설정 시간 이내로 확정한다. TEST-NET-1(192.0.2.1)은 라우팅이
    /// 보장되지 않는 문서용 주소라 침묵이 보장된다.
    /// </summary>
    [Fact]
    public async Task ConnectTimeout_SilentHost_FailsFastWithinBound()
    {
        var connector = new TcpConnector();

        DateTime started = DateTime.UtcNow;
        bool ok = await connector.ConnectAsync("192.0.2.1", 9, new TcpTransportOptions { ConnectTimeout = 1000 })
            .WaitAsync(TimeSpan.FromSeconds(4));
        DateTime finished = DateTime.UtcNow;

        Assert.False(ok); // 침묵 호스트에는 절대 연결되지 않는다.
        Assert.Null(connector.Channel);
        Assert.True(finished - started < TimeSpan.FromSeconds(2.5),
            $"연결 실패 확정이 {finished - started} 걸림 — ConnectTimeout 미적용(OS 기본 수십 초)");
    }

    /// <summary>상한을 설정해도 정상 경로(빠른 로컬 연결)는 영향받지 않는다.</summary>
    [Fact]
    public async Task ConnectTimeout_Set_DoesNotBreakFastLocalConnect()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

        var connector = new TcpConnector();
        Assert.True(await connector.ConnectAsync("127.0.0.1", port, new TcpTransportOptions { ConnectTimeout = 5000 }));
        Assert.NotNull(connector.Channel);
        connector.Channel!.Dispose();
    }
}
