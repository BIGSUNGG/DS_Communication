using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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

    /// <summary>
    /// <c>InlineDispatch=true</c>를 요청해도 메시지 단위 채널(RUDP)은 큐 디스패치를 강제한다 —
    /// 폴링 스레드는 세션 간 공유라 느린 핸들러 하나가 세션 A의 디스패치를 점유해도
    /// 세션 B의 왕복이 막히면 안 된다.
    /// </summary>
    [Fact]
    public async Task InlineDispatchOnRUDP_ForcesQueuedDispatch_OtherSessionsUnstalled()
    {
        using var listener = new RudpListener(IPAddress.Loopback, 0);

        var serverSessions = new List<RudpSession>();
        BlockingHandler? stalling = null;
        CollectHandler? collecting = null;
        listener.Accepted += channel =>
        {
            lock (serverSessions)
            {
                var queueOptions = new MessageQueueOptions { InlineDispatch = true };
                if (serverSessions.Count == 0)
                {
                    serverSessions.Add(new RudpSession(channel, new StringConverter(),
                        s => stalling = new BlockingHandler(s), queueOptions));
                }
                else
                {
                    serverSessions.Add(new RudpSession(channel, new StringConverter(),
                        s => collecting = new CollectHandler(s), queueOptions));
                }
            }
        };
        listener.Start();
        int port = listener.LocalPort;

        var connectorA = new RudpConnector();
        Assert.True(await connectorA.ConnectAsync("127.0.0.1", port));
        using var clientA = new RudpSession(connectorA.Channel!, new StringConverter(), s => new CollectHandler(s));

        var connectorB = new RudpConnector();
        Assert.True(await connectorB.ConnectAsync("127.0.0.1", port));
        using var clientB = new RudpSession(connectorB.Channel!, new StringConverter(), s => new CollectHandler(s));

        await WaitUntilAsync(() =>
        {
            lock (serverSessions) return serverSessions.Count == 2;
        });
        Assert.NotNull(stalling);
        Assert.NotNull(collecting);

        // A 핸들러를 3초 점유시킨다.
        await clientA.SendAndFlushAsync("block");
        await WaitUntilAsync(() => stalling!.Entered);

        // B 왕복은 점유와 무관해야 한다(큐 강제).
        Stopwatch stopwatch = Stopwatch.StartNew();
        await clientB.SendAndFlushAsync("ping");
        await WaitUntilAsync(() => collecting!.ReceivedCount == 1);
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(1500),
            $"B 수신이 {stopwatch.Elapsed} 걸림 — 공유 폴링 스레드가 점유됨");

        // A의 블로킹 메시지도 결국 처리된다.
        await WaitUntilAsync(() => stalling!.Completed == 1);
    }

    /// <summary>
    /// 핸들러가 밀리면 메시지 단위 채널(RUDP)은 상대방을 늦출 수 없다 — `MaxPendingMessages`를
    /// 슬롯 대기(메시지 보유)까지 포함해 강제하고, 초과 시 흐름 제어 단절(FlowControl)로 실패 폐쇄한다.
    /// 바로 전의 InlineDispatch 강제(큐 디스패치)와 합쳐져 수신 메모리 누적이 상한 안에 묶인다.
    /// </summary>
    [Fact]
    public async Task ReceiveOverflow_OnMessageChannel_DisconnectsFailClosed()
    {
        using var listener = new RudpListener(IPAddress.Loopback, 0);

        DisconnectReason? serverReason = null;
        Exception? serverError = null;
        var serverSessions = new List<RudpSession>();
        listener.Accepted += channel =>
        {
            RudpSession session = new(
                channel, new StringConverter(),
                s => new SlowHandler(s),
                new MessageQueueOptions { MaxPendingMessages = 8 });
            session.Disconnected += (_, e) =>
            {
                serverReason = e.Reason;
                serverError = e.Exception;
            };
            lock (serverSessions)
            {
                serverSessions.Add(session);
            }
        };
        listener.Start();
        int port = listener.LocalPort;

        var connector = new RudpConnector();
        Assert.True(await connector.ConnectAsync("127.0.0.1", port));
        IMessageChannel channel = connector.Channel!;
        await WaitUntilAsync(() =>
        {
            lock (serverSessions) return serverSessions.Count == 1;
        });

        // 원시 채널로 폭주 — 클라이언트 파이프라인의 백프레셔를 우회해 서버 디스패치를 압도한다.
        // 서버 핸들러는 메시지당 100ms 점유라 상한(8)을 훨씬 넘는 미처리가 쌓인다.
        try
        {
            for (int i = 0; i < 200; i++)
            {
                await channel.SendAsync(Encoding.UTF8.GetBytes($"m{i}"), default, CancellationToken.None)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch
        {
            // 단절 이후의 송신 실패는 검증 대상이 아니다.
        }

        // 흐름 제어 단절 — 무제한 누적 대신 선언된 상한 안에서 FlowControl로 끝나야 한다.
        await WaitUntilAsync(() => serverReason != null, timeoutMs: 10000);
        Assert.Equal(DisconnectReason.FlowControl, serverReason);
        Assert.NotNull(serverError);
        Assert.Contains("흐름 제어", serverError!.Message);

        lock (serverSessions)
        {
            serverSessions[0].Dispose();
        }
    }

    /// <summary>
    /// 메시지 단위 채널(RUDP)도 `MaxFrameLength`가 적용된다 — LiteNetLib은 재조립에 자체 상한이
    /// 없으므로(MaxFragmentsCount ≒ 90MB) 상한 초과 payload는 역직렬화 전에 거부하고
    /// 세션을 Error로 단절한다(바이트 경로 프레이머와 동일한 계약, 실패 폐쇄).
    /// </summary>
    [Fact]
    public async Task OversizeMessage_OnMessageChannel_DisconnectsFailClosed()
    {
        using var listener = new RudpListener(IPAddress.Loopback, 0);

        DisconnectReason? serverReason = null;
        Exception? serverError = null;
        CollectHandler? serverHandler = null;
        var serverSessions = new List<RudpSession>();
        listener.Accepted += channel =>
        {
            RudpSession session = new(
                channel, new StringConverter(),
                s => serverHandler = new CollectHandler(s),
                new MessageQueueOptions { MaxFrameLength = 256 });
            session.Disconnected += (_, e) =>
            {
                serverReason = e.Reason;
                serverError = e.Exception;
            };
            lock (serverSessions)
            {
                serverSessions.Add(session);
            }
        };
        listener.Start();
        int port = listener.LocalPort;

        var connector = new RudpConnector();
        Assert.True(await connector.ConnectAsync("127.0.0.1", port));
        IMessageChannel channel = connector.Channel!;
        await WaitUntilAsync(() =>
        {
            lock (serverSessions) return serverSessions.Count == 1;
        });

        // 상한(256)을 넘는 payload — 분할(ReliableOrdered)로 전달돼도 도달 시점에 거부된다.
        await channel.SendAsync(new byte[1024], default, CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        await WaitUntilAsync(() => serverReason != null, timeoutMs: 10000);
        Assert.Equal(DisconnectReason.Error, serverReason);
        Assert.NotNull(serverError);
        Assert.Contains("상한", serverError!.Message);
        Assert.Equal(0, serverHandler?.ReceivedCount ?? 0); // 거부된 payload는 핸들러에 도달하지 않는다.

        lock (serverSessions)
        {
            serverSessions[0].Dispose();
        }
    }

    /// <summary>
    /// 메시지 채널(RUDP) 송신도 `MaxFrameLength`를 보내기 전에 검사해 항목만 격리한다 —
    /// 초과 메시지를 보내면 수신 측(동일 상한)이 세션을 끊으므로, 와이어에 내보내기 전에
    /// 로컬 실패(flush 예외)로 끝내고 세션은 유지한다(바이트 경로와 동일 계약).
    /// </summary>
    [Fact]
    public async Task OversizeSend_OnMessageChannel_Isolated_SessionStaysConnected()
    {
        using var listener = new RudpListener(IPAddress.Loopback, 0);

        CollectHandler? serverHandler = null;
        var serverSessions = new List<RudpSession>();
        listener.Accepted += channel =>
        {
            RudpSession session = new(
                channel, new StringConverter(),
                s => serverHandler = new CollectHandler(s),
                new MessageQueueOptions { MaxFrameLength = 256 });
            lock (serverSessions)
            {
                serverSessions.Add(session);
            }
        };
        listener.Start();
        int port = listener.LocalPort;

        var connector = new RudpConnector();
        Assert.True(await connector.ConnectAsync("127.0.0.1", port));
        using var clientSession = new RudpSession(
            connector.Channel!, new StringConverter(), s => new CollectHandler(s),
            new MessageQueueOptions { MaxFrameLength = 256 });
        await WaitUntilAsync(() =>
        {
            lock (serverSessions) return serverSessions.Count == 1;
        });

        // 상한 초과 송신 — flush가 로컬에서 ArgumentException으로 끝나야 한다.
        string oversize = new string('x', 1024);
        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => clientSession.SendAndFlushAsync(oversize));
        Assert.Contains("상한", error.Message);

        // 세션은 살아 있고, 거부된 메시지는 와이어에 나가지 않는다.
        Assert.True(clientSession.IsConnected());
        Assert.Equal(0, serverHandler?.ReceivedCount ?? 0);

        // 이후 정상 송신은 왕복한다.
        await clientSession.SendAndFlushAsync("ok");
        await WaitUntilAsync(() => serverHandler?.ReceivedCount == 1);
        Assert.Equal("ok", serverHandler!.Messages[0]);
        Assert.True(clientSession.IsConnected());

        lock (serverSessions)
        {
            serverSessions[0].Dispose();
        }
    }

    /// <summary>
    /// `ConnectTimeout` 상한 — 응답 없는(블랙홀) 호스트에 대한 연결 실패를 설정 시간 이내로 확정한다.
    /// LiteNetLib 기본은 재전송 소진까지 약 5초(500ms × 10회) 고정이므로, 로컬 드롭 소켓 대상
    /// 연결이 상한(400ms) 이내에 false로 끝나는지 검증한다.
    /// </summary>
    [Fact]
    public async Task ConnectTimeout_SilentHost_FailsFastWithinBound()
    {
        // 블랙홀 — 요청을 받아 조용히 버리는 로컬 UDP 소켓. 응답이 없으므로 연결 시도는
        // 재전송 소진으로만 끝난다.
        using var blackhole = new UdpClient();
        blackhole.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)blackhole.Client.LocalEndPoint!).Port;

        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await blackhole.ReceiveAsync(); // 수신 즉시 폐기 — 응답 없음 유지.
                }
                catch
                {
                    return; // 소켓 닫힘 — 테스트 종료.
                }
            }
        });

        Stopwatch stopwatch = Stopwatch.StartNew();
        bool ok = await new RudpConnector()
            .ConnectAsync("127.0.0.1", port, new RudpTransportOptions { ConnectTimeout = 400 })
            .WaitAsync(TimeSpan.FromSeconds(4));
        stopwatch.Stop();

        Assert.False(ok); // 블랙홀에는 절대 연결되지 않는다.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2.5),
            $"연결 실패 확정이 {stopwatch.Elapsed} 걸림 — ConnectTimeout이 적용되지 않음(기본 ≈5초)");
    }

    /// <summary>
    /// peer id는 회수 후 재사용된다(LiteNetLib id 풀). 끊긴 세션의 **늦은** Dispose가
    /// 같은 id를 물려받은 새 세션의 등록부 항목을 잘못 걷어내면 슬롯이 조기 반환돼
    /// MaxConnections 상한 강제가 무너진다 — 소유자 확인 회수가 이를 막는지 검증한다.
    /// </summary>
    [Fact]
    public async Task LateDispose_AfterPeerIdReuse_DoesNotCorruptNewSession()
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
        listener.Start(new RudpTransportOptions { MaxConnections = 1, DisconnectTimeout = 300 });
        int port = listener.LocalPort;

        // 1) A 접속 — 채널 정리는 나중으로 미룬다(늦은 정리 시나리오).
        var connectorA = new RudpConnector();
        Assert.True(await connectorA.ConnectAsync("127.0.0.1", port, new RudpTransportOptions { DisconnectTimeout = 300 }));
        IMessageChannel channelA = connectorA.Channel!;
        await WaitUntilAsync(() =>
        {
            lock (accepted) return accepted.Count == 1;
        });

        // 2) A의 peer가 침묵으로 타임아웃 → 서버 회수, peer id가 풀로 반환된다.
        await WaitUntilAsync(() => listener.ActiveConnectionCount == 0);
        lock (accepted) Assert.Equal(1, accepted.Count);

        // 3) B 접속 — 동시 연결이 1개뿐이라 풀에서 A의 peer id를 그대로 물려받는다(결정적).
        var connectorB = new RudpConnector();
        Assert.True(await connectorB.ConnectAsync("127.0.0.1", port));
        IMessageChannel channelB = connectorB.Channel!;
        // 슬롯 예약(OnConnectionRequest)과 Accepted 통지(OnPeerConnected)는 다른 폴링 순간이라
        // 카운트만으로 기다리면 통지 도착 전에 검증이 달릴 수 있다 — 둘 다 기다린다.
        await WaitUntilAsync(() =>
        {
            lock (accepted) return accepted.Count == 2;
        });
        Assert.Equal(1, listener.ActiveConnectionCount);

        // B를 살려 둔다 — 리스너의 DisconnectTimeout(300ms)은 B에도 적용되므로, 침묵하면
        // B도 타임아웃돼 시나리오가 성립하지 않는다(슬롯은 B가 점유한 채여야 한다).
        using var keepaliveCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await channelB.SendAsync(Array.Empty<byte>(), default, keepaliveCts.Token);
                    await Task.Delay(100, keepaliveCts.Token);
                }
            }
            catch
            {
                // 테스트 종료·단절 — 무시.
            }
        });

        // 4) A의 늦은 Dispose — 서버 쪽 A 채널(accepted 목록의 첫 항목)이다. 같은 id를 물려받은
        //    B의 등록부 항목을 건드리면 안 된다. (클라이언트 쪽 채널의 Dispose는 서버 등록부와 무관하다.)
        IMessageChannel serverChannelA;
        lock (accepted)
        {
            serverChannelA = accepted[0];
        }
        serverChannelA.Dispose();
        await Task.Delay(300); // (버그로) 회수 경로가 동작했다면 반영될 시간.

        // 5) 결정적 판별: 상한(1)은 B가 계속 점유하므로 C는 거부되어야 한다.
        //    (버그 상태에서는 A의 Dispose가 B의 슬롯까지 반환해 C가 수락된다.)
        var connectorC = new RudpConnector();
        Assert.False(await connectorC.ConnectAsync("127.0.0.1", port).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, listener.ActiveConnectionCount);

        lock (accepted)
        {
            foreach (IMessageChannel ch in accepted)
            {
                ch.Dispose();
            }
        }
    }

    /// <summary>느린 핸들러 — 메시지마다 100ms 점유로 수신 디스패치를 압도한다.</summary>
    private sealed class SlowHandler : MessageHandler
    {
        public SlowHandler(ISession session)
            : base(session)
        {
            Register<string>(_ => Thread.Sleep(100));
        }
    }

    private sealed class BlockingHandler : MessageHandler
    {
        private int _entered;
        private int _completed;

        public BlockingHandler(ISession session)
            : base(session)
        {
            Register<string>(OnBlockingMessage);
        }

        public bool Entered => Volatile.Read(ref _entered) != 0;

        public int Completed => Volatile.Read(ref _completed);

        private void OnBlockingMessage(string message)
        {
            if (message != "block")
            {
                return;
            }

            Volatile.Write(ref _entered, 1);
            Thread.Sleep(3000); // 디스패치 점유 — 공유 폴링 스레드였다면 다른 세션이 막힌다.
            Volatile.Write(ref _completed, 1);
        }
    }

    /// <summary>
    /// 검증 키로 접속 요청만 보내고 침묵하는 공격자(핸드셰이크 미완성)도 슬롯을 영구 점유하지 않는다 —
    /// DisconnectTimeout 후 슬롯이 회수되고 정상 클라이언트는 계속 수락된다.
    /// 접속 요청 패킷은 LiteNetLib 2.1.4 와이어 형식을 직접 구성한다(프로토콜 ID 13, ConnectRequest=6).
    /// </summary>
    [Fact]
    public async Task HostileStalledHandshake_ReturnsSlotAfterTimeout()
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
        listener.Start(new RudpTransportOptions { MaxConnections = 1, DisconnectTimeout = 1000 });
        int port = listener.LocalPort;

        // 검증 키로 접속 요청만 보내고 응답하지 않는다 — 핸드셰이크를 완성하지 않는 고갈 공격 시나리오.
        using var attacker = new UdpClient();
        attacker.Connect(IPAddress.Loopback, port); // 기본 원격 지정 — 바인드·SendAsync가 여기서 결정된다.
        IPEndPoint attackerEndpoint = (IPEndPoint)attacker.Client.LocalEndPoint!;

        byte[] request = BuildConnectRequest(RudpTransportOptions.DefaultConnectionKey, attackerEndpoint);
        await attacker.SendAsync(request, request.Length);

        // 1단계: 공격자의 수락 예약이 슬롯을 잡는다.
        await WaitUntilAsync(() => listener.ActiveConnectionCount == 1);

        // 2단계: 슬롯이 잡힌 동안 정상 클라이언트는 상한 초과로 거부된다(고갈 방어가 살아 있다).
        var blocked = new RudpConnector();
        Assert.False(await blocked.ConnectAsync("127.0.0.1", port).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, listener.ActiveConnectionCount);

        // 3단계: 공격자가 침묵하면 DisconnectTimeout 후 슬롯이 돌아온다.
        await WaitUntilAsync(() => listener.ActiveConnectionCount == 0, timeoutMs: 10000);

        // 4단계: 슬롯이 진짜로 비었는지 — 정상 클라이언트가 접속하고 왕복할 수 있어야 한다.
        var connector = new RudpConnector();
        Assert.True(await connector.ConnectAsync("127.0.0.1", port));
        using var session = new RudpSession(connector.Channel!, new StringConverter(), s => new CollectHandler(s));
        Assert.True(session.IsConnected());

        // 공격자 연결이 Accepted로 전달됐다면 세션 미생성 상태로 남는다 — 정리하고 수락 경로에 남기지 않는다.
        lock (accepted)
        {
            foreach (IMessageChannel channel in accepted)
            {
                channel.Dispose();
            }

            accepted.Clear();
        }
    }

    /// <summary>
    /// 잘못된 키 접속 폭주(거절 재시도 포함)도 슬롯 파편을 남기지 않는다 —
    /// ActiveConnectionCount는 항상 0으로 복귀하고, 이후 정상 수락이 가능해야 한다.
    /// </summary>
    [Fact]
    public async Task WrongKeyFlood_LeavesNoSlotResidue()
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
        listener.Start(new RudpTransportOptions { MaxConnections = 1, ConnectionKey = "secret" });
        int port = listener.LocalPort;

        // 틀린 키로 여러 번 접속 시도 — 전부 거절되어야 하고 슬롯에 파편을 남기지 않아야 한다.
        for (int i = 0; i < 4; i++)
        {
            var bad = new RudpConnector();
            Assert.False(await bad.ConnectAsync("127.0.0.1", port, new RudpTransportOptions
            {
                ConnectionKey = $"wrong-key-{i}",
            }).WaitAsync(TimeSpan.FromSeconds(10)));
        }

        // 거절된 시도가 슬롯에 흔적을 남겼다면 여기서 걸린다.
        await WaitUntilAsync(() => listener.ActiveConnectionCount == 0);

        // 올바른 키로는 여전히 접속된다.
        var good = new RudpConnector();
        Assert.True(await good.ConnectAsync("127.0.0.1", port, new RudpTransportOptions { ConnectionKey = "secret" }));
        using var session = new RudpSession(good.Channel!, new StringConverter(), s => new CollectHandler(s));
        Assert.True(session.IsConnected());

        await WaitUntilAsync(() =>
        {
            lock (accepted) return accepted.Count == 1;
        });
        Assert.Equal(1, listener.ActiveConnectionCount);
        lock (accepted) Assert.Single(accepted);
    }

    /// <summary>
    /// LiteNetLib 2.1.4 접속 요청 패킷 구성: [0]=ConnectRequest(6)·connectNum 0,
    /// [1..4]=프로토콜 ID 13, [5..12]=connectTime, [13..16]=peerId,
    /// [17]=주소 크기, [18..]=IPv4 SocketAddress, 뒤에 키 문자열(ushort 길이+1, UTF-8).
    /// </summary>
    private static byte[] BuildConnectRequest(string key, IPEndPoint attackerEndpoint)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        SocketAddress address = new IPEndPoint(IPAddress.Loopback, attackerEndpoint.Port).Serialize();

        int dataOffset = 18 + address.Size;
        byte[] packet = new byte[dataOffset + 2 + keyBytes.Length];
        packet[0] = 0x06; // PacketProperty.ConnectRequest, 연결 번호 0
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(1, 4), 13); // NetConstants.ProtocolId
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(5, 8), DateTime.UtcNow.Ticks);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(13, 4), 0x12345678); // 자기 peer id
        packet[17] = (byte)address.Size;
        for (int i = 0; i < address.Size; i++)
        {
            packet[18 + i] = address[i];
        }

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(dataOffset, 2), (ushort)(keyBytes.Length + 1));
        Buffer.BlockCopy(keyBytes, 0, packet, dataOffset + 2, keyBytes.Length);
        return packet;
    }
}
