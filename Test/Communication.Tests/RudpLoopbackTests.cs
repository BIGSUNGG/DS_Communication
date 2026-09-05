using System.Collections.Concurrent;
using System.Net;
using Communication.Network.RUDP;
using Communication.Shared.Channels;
using Communication.Shared.Connection;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using Xunit;

namespace Communication.Tests;

public class RudpLoopbackTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 15000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("조건이 시간 안에 만족되지 않았습니다.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class CollectHandler : MessageHandler
    {
        private readonly List<object> _received = new();

        public CollectHandler(ISession session)
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

        public IReadOnlyList<object> Messages
        {
            get
            {
                lock (_received)
                {
                    return _received.ToList();
                }
            }
        }

        private void OnMessage(string message)
        {
            lock (_received)
            {
                _received.Add(message);
            }
        }
    }

    /// <summary>연결 → 양방향 송수신 → 끊김 원인(Local/Remote).</summary>
    [Fact]
    public async Task Connect_SendBothDirections_Disconnect_RaisesReasons()
    {
        using var listener = new RudpListener(IPAddress.Loopback, 0);

        RudpSession? serverSession = null;
        CollectHandler? serverHandler = null;
        listener.Accepted += channel =>
            serverSession = new RudpSession(channel, new StringConverter(), session =>
            {
                serverHandler = new CollectHandler(session);
                return serverHandler;
            });
        listener.Start();
        int port = listener.LocalPort;
        Assert.True(port > 0, "포트 0 바인딩은 실제 포트를 노출해야 합니다.");

        var connector = new RudpConnector();
        Assert.True(await connector.ConnectAsync("127.0.0.1", port));

        CollectHandler? clientHandler = null;
        using var clientSession = new RudpSession(connector.Channel!, new StringConverter(), session =>
        {
            clientHandler = new CollectHandler(session);
            return clientHandler;
        });

        await WaitUntilAsync(() => serverSession != null);

        await clientSession.SendAndFlushAsync("ping");
        await WaitUntilAsync(() => serverHandler?.ReceivedCount == 1);
        Assert.Equal("ping", serverHandler!.Messages[0]);

        await serverSession!.SendAndFlushAsync("pong");
        await WaitUntilAsync(() => clientHandler?.ReceivedCount == 1);
        Assert.Equal("pong", clientHandler!.Messages[0]);

        DisconnectReason? clientReason = null;
        DisconnectReason? serverReason = null;
        clientSession.Disconnected += (_, e) => clientReason = e.Reason;
        serverSession.Disconnected += (_, e) => serverReason = e.Reason;

        clientSession.Disconnect();

        await WaitUntilAsync(() => clientReason != null && serverReason != null);
        Assert.Equal(DisconnectReason.Local, clientReason);
        Assert.Equal(DisconnectReason.Remote, serverReason); // 원격 끊김은 peer 끊김 통지가 세션으로 이어붙는다.
    }

    /// <summary>리스너 1대에 동시 클라이언트 4개 — 전부 접속·왕복하고 접속 수가 정확히 관리된다.</summary>
    [Fact]
    public async Task FourConcurrentClients_OnSingleListener_AllRoundTrip()
    {
        const int clientCount = 4;
        using var listener = new RudpListener(IPAddress.Loopback, 0);

        var serverSessions = new List<RudpSession>();
        var serverHandlers = new List<CollectHandler>();
        listener.Accepted += channel =>
        {
            CollectHandler? handler = null;
            RudpSession session = new(channel, new StringConverter(), s =>
            {
                handler = new CollectHandler(s);
                return handler;
            });

            lock (serverSessions)
            {
                serverSessions.Add(session);
                serverHandlers.Add(handler!);
            }
        };
        listener.Start();
        int port = listener.LocalPort;

        var clientSessions = new List<RudpSession>();
        try
        {
            for (int i = 0; i < clientCount; i++)
            {
                var connector = new RudpConnector();
                Assert.True(await connector.ConnectAsync("127.0.0.1", port), $"클라이언트 {i} 접속 실패");

                RudpSession session = new(connector.Channel!, new StringConverter(), s => new CollectHandler(s));
                clientSessions.Add(session);
                await session.SendAndFlushAsync($"hello-{i}");
            }

            await WaitUntilAsync(() =>
            {
                lock (serverSessions) return serverSessions.Count == clientCount;
            });
            Assert.Equal(clientCount, listener.ActiveConnectionCount);

            // 각 클라이언트의 메시지가 서버 쪽 어느 세션에든 전부 도착해야 한다.
            await WaitUntilAsync(() =>
            {
                lock (serverHandlers) return serverHandlers.Sum(h => h.ReceivedCount) == clientCount;
            });

            var received = new List<string>();
            lock (serverHandlers)
            {
                foreach (CollectHandler handler in serverHandlers)
                {
                    received.AddRange(handler.Messages.Cast<string>());
                }
            }

            received.Sort(StringComparer.Ordinal);
            Assert.Equal(Enumerable.Range(0, clientCount).Select(i => $"hello-{i}").ToArray(), received);

            // 채널 회수 → 접속 수 감소.
            clientSessions[0].Dispose();
            await WaitUntilAsync(() => listener.ActiveConnectionCount == clientCount - 1);
        }
        finally
        {
            foreach (RudpSession session in clientSessions)
            {
                session.Dispose();
            }

            lock (serverSessions)
            {
                foreach (RudpSession session in serverSessions)
                {
                    session.Dispose();
                }
            }
        }
    }

    /// <summary>메시지별로 다른 전송 방식을 지정해 전부 왕복한다 — 5개 방식 각각 메시지 1개씩.</summary>
    [Fact]
    public async Task SendMessage_PerMessageDeliveryMethod_AllRoundTrip()
    {
        using var listener = new RudpListener(IPAddress.Loopback, 0);

        RudpSession? serverSession = null;
        CollectHandler? serverHandler = null;
        listener.Accepted += channel =>
            serverSession = new RudpSession(channel, new StringConverter(), session =>
            {
                serverHandler = new CollectHandler(session);
                return serverHandler;
            });
        listener.Start();
        int port = listener.LocalPort;

        var connector = new RudpConnector();
        Assert.True(await connector.ConnectAsync("127.0.0.1", port));
        using var clientSession = new RudpSession(connector.Channel!, new StringConverter(), s => new CollectHandler(s));
        await WaitUntilAsync(() => serverSession != null);

        RudpDeliveryMethod[] methods =
        {
            RudpDeliveryMethod.ReliableOrdered,
            RudpDeliveryMethod.ReliableUnordered,
            RudpDeliveryMethod.Sequenced,
            RudpDeliveryMethod.ReliableSequenced,
            RudpDeliveryMethod.Unreliable,
        };

        // 한 번에 하나씩 보내고 도착을 확인한다 — Sequenced의 순서 유실 경쟁을 제거.
        for (int i = 0; i < methods.Length; i++)
        {
            await clientSession.SendAndFlushAsync($"msg-{methods[i]}", new RudpSendOptions(methods[i]));
            int expected = i + 1;
            await WaitUntilAsync(() => serverHandler?.ReceivedCount == expected);
        }

        Assert.Equal(methods.Select(m => $"msg-{m}").ToArray(), serverHandler!.Messages.Cast<string>().ToArray());
        Assert.True(clientSession.IsConnected());
        Assert.True(serverSession!.IsConnected());

        // 공용 인스턴스도 같은 방식으로 동작한다(송신 경로 할당 0).
        await clientSession.SendAndFlushAsync("via-static", RudpSendOptions.Unreliable);
        await WaitUntilAsync(() => serverHandler.ReceivedCount == methods.Length + 1);
    }

    /// <summary>
    /// 분할 불가 방식(Sequenced·ReliableSequenced·Unreliable)으로 MTU 초과 payload를 보내면 ArgumentException,
    /// 분할 가능 방식(ReliableOrdered)은 같은 크기가 실제로 도착한다.
    /// </summary>
    [Fact]
    public async Task Send_OversizePayload_NonFragmentableMethod_ThrowsArgumentException()
    {
        using var listener = new RudpListener(IPAddress.Loopback, 0);

        var receivedLengths = new ConcurrentQueue<int>();
        IMessageChannel? serverChannel = null;
        listener.Accepted += channel =>
        {
            serverChannel = channel;
            channel.MessageReceived += payload => receivedLengths.Enqueue(payload.Length);
        };
        listener.Start();
        int port = listener.LocalPort;

        var connector = new RudpConnector();
        Assert.True(await connector.ConnectAsync("127.0.0.1", port));
        IMessageChannel clientChannel = connector.Channel!;
        await WaitUntilAsync(() => serverChannel != null);

        byte[] oversize = new byte[8192]; // loopback 초기 MTU(1024) 기준 단일 패킷 상한을 넉넉히 넘김.

        RudpSendOptions[] nonFragmentable =
        {
            RudpSendOptions.Sequenced,
            RudpSendOptions.ReliableSequenced,
            RudpSendOptions.Unreliable,
        };

        foreach (RudpSendOptions options in nonFragmentable)
        {
            ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
                () => clientChannel.SendAsync(oversize, options).AsTask());
            Assert.Contains("분할", error.Message); // LiteNetLib 내부 예외가 아니라 채널의 사전 검사여야 한다.
        }

        Assert.Empty(receivedLengths); // 거부된 payload는 와이어에 나가지 않는다.

        await clientChannel.SendAsync(oversize, RudpSendOptions.ReliableOrdered);
        await WaitUntilAsync(() => !receivedLengths.IsEmpty);
        Assert.True(receivedLengths.TryDequeue(out int length));
        Assert.Equal(oversize.Length, length); // 분할 → 재조립.

        Assert.True(clientChannel.IsConnected); // 채널 단위 검증 — 세션 끊김으로 격상되지 않는다.

        clientChannel.Dispose();
        serverChannel!.Dispose();
    }

    /// <summary>MaxConnections 상한 초과 접속 요청은 즉시 거부되고 Accepted로 통지되지 않으며, 슬롯은 회수된다.</summary>
    [Fact]
    public async Task MaxConnections_OverLimitRequest_IsRejected()
    {
        using var listener = new RudpListener(IPAddress.Loopback, 0);

        var accepted = new List<IMessageChannel>();
        listener.Accepted += channel =>
        {
            lock (accepted)
            {
                accepted.Add(channel);
            }
        };
        listener.Start(new RudpTransportOptions { MaxConnections = 2 });
        int port = listener.LocalPort;

        var sessions = new List<RudpSession>();
        try
        {
            for (int i = 0; i < 2; i++)
            {
                var connector = new RudpConnector();
                Assert.True(await connector.ConnectAsync("127.0.0.1", port));
                sessions.Add(new RudpSession(connector.Channel!, new StringConverter(), s => new CollectHandler(s)));
            }

            await WaitUntilAsync(() =>
            {
                lock (accepted) return accepted.Count == 2;
            });
            Assert.Equal(2, listener.ActiveConnectionCount);

            // 상한 초과 — 접속 요청은 거부되고 Accepted 통지도 없다.
            // 거부는 재시도 소진을 기다리지 않고 클라이언트에 바로 전달돼 ConnectAsync가 false로 끝난다.
            var rejected = new RudpConnector();
            Assert.False(await rejected.ConnectAsync("127.0.0.1", port).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Null(rejected.Channel);
            Assert.Equal(2, listener.ActiveConnectionCount);
            lock (accepted) Assert.Equal(2, accepted.Count);

            // 슬롯 회수 후 재수락.
            lock (accepted) accepted[0].Dispose();
            await WaitUntilAsync(() => listener.ActiveConnectionCount == 1);

            var third = new RudpConnector();
            Assert.True(await third.ConnectAsync("127.0.0.1", port));
            sessions.Add(new RudpSession(third.Channel!, new StringConverter(), s => new CollectHandler(s)));
            await WaitUntilAsync(() =>
            {
                lock (accepted) return accepted.Count == 3;
            });
            Assert.Equal(2, listener.ActiveConnectionCount);
        }
        finally
        {
            foreach (RudpSession session in sessions)
            {
                session.Dispose();
            }

            lock (accepted)
            {
                foreach (IMessageChannel channel in accepted)
                {
                    channel.Dispose();
                }
            }
        }
    }
}
