using System;
using System.Net;
using Communication.Shared.Channels;

namespace Communication.Network.RUDP;

/// <summary>
/// RUDP 수락. 수락된 채널은 <see cref="Accepted"/>로 전달하며, 세션 생성은 앱이 한다.
/// <c>RudpTransportOptions.MaxConnections</c> 설정 시 상한을 넘는 접속 요청은 즉시 거부되고 수락은 계속된다.
/// </summary>
/// <remarks>
/// 접속 수와 무관하게 전용 폴링 스레드 1개만 돌린다. 수신은 세션별 디스패치 큐로 넘어가므로
/// 앱 핸들러가 폴링 스레드를 점유하지 않는다.
/// </remarks>
public sealed class RudpListener : IDisposable
{
    private readonly IPAddress _address;
    private readonly int _port;
    private RudpNetHost? _host;

    public RudpListener(IPAddress address, int port)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _port = port;
    }

    /// <summary>수락된 채널 통지. 세션 생성 등 앱 로직에서 던진 예외는 격리된다.</summary>
    public event Action<IMessageChannel>? Accepted;

    /// <summary>바인딩된 실제 포트. 포트 0(임시 포트) 수락 시 테스트·등록에 사용한다.</summary>
    public int LocalPort => _host?.LocalPort ?? 0;

    /// <summary>수락됐으나 아직 회수되지 않은 채널 수. <c>MaxConnections</c> 상한 강제의 기준이다.</summary>
    public int ActiveConnectionCount => _host?.ActiveConnectionCount ?? 0;

    /// <exception cref="InvalidOperationException">이미 시작됐거나 바인딩에 실패한 경우.</exception>
    public void Start(RudpTransportOptions? options = null)
    {
        if (_host != null)
        {
            throw new InvalidOperationException("이미 시작된 리스너입니다.");
        }

        RudpNetHost host = new(options);
        host.PeerAccepted = channel =>
        {
            // 수락마다 최신 구독자를 읽는다 — Start 이후 구독자도 채널을 받는다(TcpListener와 동일 계약).
            Action<IMessageChannel>? accepted = Accepted;
            if (accepted is null)
            {
                channel.Dispose(); // 구독자 없음 — 연결이 새지 않도록 정리 후 수락 계속.
                return;
            }

            accepted.Invoke(channel); // 예외는 호스트가 격리하고 채널을 정리한다.
        };

        if (!host.StartServer(_address, _port))
        {
            host.Dispose();
            throw new InvalidOperationException($"RUDP 리스너 바인딩 실패 ({_address}:{_port}).");
        }

        _host = host;
    }

    public void Stop()
    {
        RudpNetHost? host = _host;
        _host = null;
        host?.Dispose(); // NetManager.Stop(true)가 접속 중인 peer에 끊김 메시지를 보낸 뒤 스레드를 정리한다.
    }

    public void Dispose() => Stop();
}
