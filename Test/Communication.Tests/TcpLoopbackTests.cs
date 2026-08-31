using System.Net;
using Communication.Network.TCP;
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
}
