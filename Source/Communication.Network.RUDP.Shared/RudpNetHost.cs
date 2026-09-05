using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Utils;
using SharedDisconnectReason = Communication.Shared.Connection.DisconnectReason;

namespace Communication.Network.RUDP;

/// <summary>
/// LiteNetLib <c>NetManager</c> 소유자 — 전용 폴링 루프 1개, peer ↔ 채널 등록부, 접속 수락 정책.
/// <c>RudpListener</c>·<c>RudpConnector</c>가 공용으로 쓰는 내부 호스트이며,
/// LiteNetLib 타입은 이 타입과 <see cref="RudpMessageChannel"/> 안에서만 등장한다.
/// </summary>
/// <remarks>
/// <b>병목 구조</b>: 소켓 수신은 LiteNetLib 내부 스레드가 담당하고, 이 호스트는
/// <b>클라이언트 수와 무관하게 폴링 스레드 1개</b>만 돌려 이벤트를 드레인한다.
/// 수신 payload는 채널 → <c>MessagePipeline</c>의 세션별 디스패치 큐로 넘어가므로
/// 앱 핸들러는 폴링 스레드에서 실행되지 않는다 — 느린 클라이언트 1개가 다른 접속을 막지 못한다.
/// </remarks>
internal sealed class RudpNetHost : INetEventListener, IDisposable
{
    // ponytail: 폴링 간격 고정 1ms. 수신 지연이 실제로 문제되면 RudpTransportOptions로 노출.
    private const int PollIntervalMs = 1;

    private readonly NetManager _manager;
    private readonly ConcurrentDictionary<int, RudpMessageChannel> _channels = new();
    private readonly string _connectionKey;
    private readonly int? _maxConnections;
    private Thread? _pollThread;
    private volatile bool _running;
    private bool _isServer;
    private int _connectionCount; // 수락 예약 후 미회수 slot — MaxConnections 강제 기준
    private int _disposed;
    private long _lastPollErrorTick; // 반복 폴링 오류 로그 플러드 방지(초당 1회 기록)

    internal RudpNetHost(RudpTransportOptions? options)
    {
        _connectionKey = options?.ConnectionKey ?? RudpTransportOptions.DefaultConnectionKey;
        _maxConnections = options?.MaxConnections;
        _manager = new NetManager(this, null)
        {
            DisconnectTimeout = options?.DisconnectTimeout ?? RudpTransportOptions.DefaultDisconnectTimeoutMs,
            IPv6Enabled = options?.IPv6 ?? false,
        };

        // 연결 시도 상한: 침묵 호스트(블랙홀)에 대한 실패를 설정된 시간 이내로 끌어당긴다.
        // LiteNetLib 기본은 500ms × 10회 ≈ 5초 고정 — 재전송 간격 100ms로 환산해 횟수 상한을 건다.
        if (options?.ConnectTimeout is { } connectTimeoutMs)
        {
            _manager.ReconnectDelay = 100;
            _manager.MaxConnectAttempts = Math.Max(1, connectTimeoutMs / 100);
        }
    }

    /// <summary>바인딩된 실제 포트. 포트 0(임시 포트) 수락·연결 시 테스트·등록에 사용한다.</summary>
    internal int LocalPort => _manager.LocalPort;

    /// <summary>수락됐으나 아직 회수되지 않은 채널 수. <c>MaxConnections</c> 상한 강제의 기준이다.</summary>
    internal int ActiveConnectionCount => Volatile.Read(ref _connectionCount);

    /// <summary>연결이 확립된 peer마다 채널을 만들어 통지한다. 서버는 수락, 클라이언트는 연결 완료가 여기로 온다.</summary>
    internal Action<RudpMessageChannel>? PeerAccepted { get; set; }

    /// <summary>채널로 등록되기 전에 peer가 끊긴 경우 통지 — 클라이언트 연결 실패 판정에 쓴다.</summary>
    internal Action? PeerFailed { get; set; }

    /// <summary>서버로 시작한다. 접속 요청은 <c>MaxConnections</c>·연결 키 검사 후 수락한다.</summary>
    internal bool StartServer(IPAddress address, int port)
    {
        _isServer = true;
        bool isIPv6 = address.AddressFamily == AddressFamily.InterNetworkV6;
        if (!_manager.Start(isIPv6 ? IPAddress.Any : address, isIPv6 ? address : IPAddress.IPv6Any, port))
        {
            return false;
        }

        StartPollLoop();
        return true;
    }

    /// <summary>클라이언트로 시작한다(임시 로컬 포트 바인딩). 들어오는 접속 요청은 거부된다.</summary>
    internal bool StartClient()
    {
        _isServer = false;
        if (!_manager.Start())
        {
            return false;
        }

        StartPollLoop();
        return true;
    }

    /// <summary>연결 요청을 보낸다. 완료 여부는 <see cref="PeerAccepted"/>·<see cref="PeerFailed"/>로 온다.</summary>
    internal bool RequestConnect(string host, int port)
    {
        try
        {
            _manager.Connect(host, port, _connectionKey);
            return true;
        }
        catch (Exception e)
        {
            // 호스트 해석 실패·소켓 오류 — 연결 불가로 취급한다.
            Trace.TraceError($"RUDP 연결 요청 실패 ({host}:{port}): {e}");
            return false;
        }
    }

    internal void Stop()
    {
        // NetManager를 먼저 정지시킨다 — Stop(true)가 접속 중인 peer에 끊김 메시지를 보내 상대가
        // 타임아웃이 아니라 RemoteConnectionClose로 끊김을 본다. 이후 폴링 스레드 정리는 무해하다.
        try
        {
            _manager.Stop(true);
        }
        catch (Exception e)
        {
            Trace.TraceError($"RUDP NetManager 정지 실패 — 무시: {e}");
        }

        _running = false;

        Thread? pollThread = _pollThread;
        _pollThread = null;
        if (pollThread != null && pollThread != Thread.CurrentThread)
        {
            pollThread.Interrupt();
            pollThread.Join(500); // 폴링 간격이 1ms라 정상적으로는 즉시 끝난다.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Stop();
        _channels.Clear();
    }

    /// <summary>
    /// 채널을 등록부에서 회수하고 세션에 끊김을 통지한다.
    /// <c>TryRemove</c>가 정확히 1회 가드 — 로컬 Dispose와 원격 끊김이 경쟁해도 카운트·통지는 한 번만 일어난다.
    /// </summary>
    internal void ReleaseChannel(RudpMessageChannel channel, SharedDisconnectReason reason)
    {
        // peer id는 회수 후 재사용된다(GetNextPeerId 풀). 소유자 확인 없이 id로만 지우면
        // 늦게 도착한 이전 채널의 Dispose가 새 세션의 등록부 항목을 잘못 걷어낸다 —
        // 슬롯 카운트 조기 반환(상한 강제 무너짐)과 새 채널의 고아화가 동시에 일어난다.
        // 현재 소유자가 이 채널일 때만 회수한다.
        if (!_channels.TryGetValue(channel.PeerId, out RudpMessageChannel? current) || !ReferenceEquals(current, channel))
        {
            return;
        }

        if (!_channels.TryRemove(channel.PeerId, out _))
        {
            return; // 사이에 다른 스레드가 회수 — 카운트·통지는 그쪽이 담당.
        }

        if (_isServer)
        {
            Interlocked.Decrement(ref _connectionCount);
        }

        channel.NotifyTransportDisconnected(reason);
    }

    private void StartPollLoop()
    {
        _running = true;
        _pollThread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = _isServer ? "RUDP listener poll" : "RUDP connector poll",
        };
        _pollThread.Start();
    }

    /// <summary>전용 폴링 스레드 — 이 루프가 호스트당 하나뿐이라 스레드 수가 접속 수를 따라 늘지 않는다.</summary>
    private void PollLoop()
    {
        while (_running)
        {
            try
            {
                _manager.PollEvents();
            }
            catch (Exception e)
            {
                // 폴링 예외가 스레드를 죽이면 모든 접속의 수신이 멈춘다 — 격리 후 계속.
                // 반복 실패(1ms 재시도)가 로그를 도배하지 않도록 동일 오류는 초당 1회만 기록한다.
                long now = DateTime.UtcNow.Ticks;
                long last = Interlocked.Read(ref _lastPollErrorTick);
                if (now - last >= TimeSpan.TicksPerSecond && Interlocked.CompareExchange(ref _lastPollErrorTick, now, last) == last)
                {
                    Trace.TraceError($"RUDP 폴링 예외 — 격리 후 계속: {e}");
                }
            }

            try
            {
                Thread.Sleep(PollIntervalMs);
            }
            catch (ThreadInterruptedException)
            {
                return; // Stop()이 깨움 — 탈출.
            }
        }
    }

    // ---- INetEventListener (폴링 스레드에서 호출됨) ----

    public void OnConnectionRequest(ConnectionRequest request)
    {
        if (!_isServer)
        {
            request.Reject(); // 클라이언트는 들어오는 접속을 받지 않는다 — 임시 포트 보호.
            return;
        }

        // 수락 전에 슬롯을 예약한다 — 같은 폴링 배치의 여러 요청이 상한을 함께 넘는 경쟁을 막는다.
        int active = Interlocked.Increment(ref _connectionCount);
        if (_maxConnections is { } max && active > max)
        {
            Interlocked.Decrement(ref _connectionCount);
            request.Reject();
            return;
        }

        if (request.AcceptIfKey(_connectionKey) is null)
        {
            Interlocked.Decrement(ref _connectionCount); // 키 불일치 — 수락되지 않았으므로 슬롯 반환.
        }
    }

    public void OnPeerConnected(NetPeer peer)
    {
        // 클라이언트 호스트는 peer가 하나뿐이라 채널이 호스트(폴링 스레드·NetManager)까지 소유한다 —
        // 앱이 세션/채널만 정리해도 남는 자원이 없다. 서버는 여러 peer가 한 호스트를 공유하므로 소유하지 않는다.
        RudpMessageChannel channel = new(this, peer, ownsHost: !_isServer);
        _channels[peer.Id] = channel;

        // 수락마다 최신 구독자를 읽는다 — Start 이후 구독자도 채널을 받는다(TcpListener와 동일 계약).
        Action<RudpMessageChannel>? accepted = PeerAccepted;
        if (accepted is null)
        {
            channel.Dispose(); // 구독자 없음 — 연결이 새지 않도록 정리.
            return;
        }

        try
        {
            accepted.Invoke(channel);
        }
        catch (Exception e)
        {
            Trace.TraceError($"RUDP 수락 핸들러 예외 — 채널 정리 후 계속: {e}");
            channel.Dispose();
        }
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (_channels.TryGetValue(peer.Id, out RudpMessageChannel? channel))
        {
            ReleaseChannel(channel, MapDisconnectReason(disconnectInfo.Reason));
            return;
        }

        PeerFailed?.Invoke(); // 채널이 되기 전 끊김 — 클라이언트 연결 실패.
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        try
        {
            if (_channels.TryGetValue(peer.Id, out RudpMessageChannel? channel))
            {
                // payload는 콜백 안에서만 유효하다(IMessageChannel 계약) — 파이프라인이 여기서 역직렬화해 복사한다.
                channel.DeliverReceived(reader.GetRemainingBytesMemory());
            }
        }
        catch (Exception e)
        {
            Trace.TraceError($"RUDP 수신 처리 예외 — 격리 후 계속: {e}");
        }
        finally
        {
            reader.Recycle(); // AutoRecycle=false — 호출자가 풀에 반환해야 한다.
        }
    }

    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        => Trace.TraceError($"RUDP 소켓 오류 ({endPoint}): {socketError}");

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        => reader.Recycle(); // 연결 없는 메시지는 받지 않는다 — 풀 반환만.

    public void OnMessageDelivered(NetPeer peer, object userData)
    {
    }

    public void OnNtpResponse(NtpPacket packet)
    {
    }

    public void OnPeerAddressChanged(NetPeer peer, IPEndPoint previousAddress)
    {
    }

    private static SharedDisconnectReason MapDisconnectReason(DisconnectReason reason) => reason switch
    {
        DisconnectReason.Timeout => SharedDisconnectReason.Timeout,
        DisconnectReason.RemoteConnectionClose => SharedDisconnectReason.Remote,
        DisconnectReason.DisconnectPeerCalled => SharedDisconnectReason.Local,
        _ => SharedDisconnectReason.Error, // ConnectionFailed·HostUnreachable·InvalidProtocol·UnknownHost 등
    };
}
