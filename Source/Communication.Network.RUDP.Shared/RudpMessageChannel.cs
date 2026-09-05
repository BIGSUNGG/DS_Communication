using System;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;
using LiteNetLib;
using SharedDisconnectReason = Communication.Shared.Connection.DisconnectReason;

namespace Communication.Network.RUDP;

/// <summary>
/// peer 1개와 대응하는 메시지 단위 채널. LiteNetLib peer를 감싸며, 전송이 메시지 경계를 제공하므로
/// 프레이머가 필요 없다. 앱은 이 타입을 직접 만들지 않는다 — <c>RudpConnector.Channel</c> 또는
/// <c>RudpListener.Accepted</c>로 받는다.
/// </summary>
public sealed class RudpMessageChannel : IMessageChannel
{
    private readonly NetPeer _peer;
    private readonly RudpNetHost _host;
    private readonly bool _ownsHost; // 클라이언트 채널만 true — 호스트(폴링 스레드·NetManager)까지 정리
    private int _disposed; // 0 = 사용 가능, 1 = 정리됨

    internal RudpMessageChannel(RudpNetHost host, NetPeer peer, bool ownsHost)
    {
        _host = host;
        _peer = peer;
        _ownsHost = ownsHost;
    }

    /// <summary>
    /// 전송 계층 관점의 연결 상태. 채널이 정리되지 않았고 peer가 <c>Connected</c>일 때만 <c>true</c>.
    /// </summary>
    public bool IsConnected
        => Volatile.Read(ref _disposed) == 0 && _peer.ConnectionState == ConnectionState.Connected;

    /// <summary>
    /// 수신 메시지 알림. RUDP 호스트의 폴링 스레드에서 발생하며, payload는 콜백 안에서만 유효하다.
    /// </summary>
    public event Action<ReadOnlyMemory<byte>>? MessageReceived;

    /// <summary>
    /// peer 끊김 통지. <c>RudpSession</c>이 구독해 <c>Session.Disconnected</c>로 이어 붙인다 —
    /// 메시지 채널 경로에는 수신 루프가 없어 원격 끊김을 이 통지가 대신 전달한다.
    /// </summary>
    internal event Action<SharedDisconnectReason>? TransportDisconnected;

    internal int PeerId => _peer.Id;

    /// <summary>
    /// payload 하나를 지정한 전송 방식으로 보낸다. <paramref name="options"/>가 <c>null</c>이거나
    /// <see cref="RudpSendOptions"/>가 아니면 <see cref="RudpDeliveryMethod.ReliableOrdered"/>로 보낸다.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// 분할(fragmentation)이 불가능한 방식(<see cref="RudpDeliveryMethod.Sequenced"/>,
    /// <see cref="RudpDeliveryMethod.ReliableSequenced"/>, <see cref="RudpDeliveryMethod.Unreliable"/>)으로
    /// MTU 초과 payload를 보내려 한 경우. 조용한 유실 대신 즉시 실패한다.
    /// </exception>
    /// <remarks>
    /// 이 예외는 <c>MessagePipeline</c>의 송신 루프에서 채널 오류로 취급되어 해당 항목의 flush를
    /// 예외 완료시키고 세션을 <c>Disconnected(Error)</c>로 끊는다(<see cref="Exception"/>에 원인이 보존된다).
    /// </remarks>
    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Faulted(new OperationCanceledException(cancellationToken));
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return Faulted(new InvalidOperationException("채널이 정리되어 송신할 수 없습니다."));
        }

        RudpDeliveryMethod requested = (options as RudpSendOptions)?.DeliveryMethod ?? RudpDeliveryMethod.ReliableOrdered;

        DeliveryMethod method;
        try
        {
            method = ToDeliveryMethod(requested);
        }
        catch (Exception e)
        {
            return Faulted(e);
        }

        // 분할 가능한 방식(ReliableOrdered·ReliableUnordered)은 크기 제한을 두지 않는다.
        if (!CanFragment(method))
        {
            int limit = _peer.GetMaxSinglePacketSize(method);
            if (payload.Length > limit)
            {
                return Faulted(new ArgumentException(
                    $"전송 방식 {requested}는 분할을 지원하지 않아 payload가 {limit}바이트 이하여야 합니다 (요청 {payload.Length}바이트). " +
                    $"더 큰 메시지는 {nameof(RudpDeliveryMethod.ReliableOrdered)} 또는 {nameof(RudpDeliveryMethod.ReliableUnordered)}로 보내십시오."));
            }
        }

        try
        {
            _peer.Send(payload.Span, 0, method);
            return default;
        }
        catch (Exception e)
        {
            return Faulted(e);
        }
    }

    /// <summary>peer를 끊고 호스트에서 채널을 회수한다. 중복 호출은 무시된다.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // 세션 소유 경로: 세션이 이미 Local로 끊김을 기록했으므로 이 통지는 중복 가드에 걸려 무시된다.
        _host.ReleaseChannel(this, SharedDisconnectReason.Local);

        try
        {
            _peer.Disconnect();
        }
        catch
        {
            // 끊기 실패가 정리를 막으면 안 된다.
        }

        if (_ownsHost)
        {
            _host.Dispose(); // NetManager 정지(true)가 끊김 메시지를 내보낸 뒤 스레드를 정리한다.
        }
    }

    /// <summary>호스트 폴링 스레드가 수신을 전달한다. 콜백 안에서만 payload가 유효하다.</summary>
    internal void DeliverReceived(ReadOnlyMemory<byte> payload) => MessageReceived?.Invoke(payload);

    /// <summary>호스트가 peer 끊김을 전달한다. 세션당 1회는 <c>Session</c> 쪽 가드가 보장한다.</summary>
    internal void NotifyTransportDisconnected(SharedDisconnectReason reason) => TransportDisconnected?.Invoke(reason);

    /// <summary>분할 지원 여부 — LiteNetLib에서 ReliableOrdered·ReliableUnordered만 분할된다.</summary>
    private static bool CanFragment(DeliveryMethod method)
        => method is DeliveryMethod.ReliableOrdered or DeliveryMethod.ReliableUnordered;

    // netstandard2.1에는 ValueTask.FromException이 없다(.NET 5+) — Task 경유로 예외 완료 ValueTask를 만든다.
    private static ValueTask Faulted(Exception error) => new(Task.FromException(error));

    private static DeliveryMethod ToDeliveryMethod(RudpDeliveryMethod method) => method switch
    {
        RudpDeliveryMethod.ReliableUnordered => DeliveryMethod.ReliableUnordered,
        RudpDeliveryMethod.Sequenced => DeliveryMethod.Sequenced,
        RudpDeliveryMethod.ReliableOrdered => DeliveryMethod.ReliableOrdered,
        RudpDeliveryMethod.ReliableSequenced => DeliveryMethod.ReliableSequenced,
        RudpDeliveryMethod.Unreliable => DeliveryMethod.Unreliable,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "알 수 없는 RUDP 전송 방식입니다."),
    };
}
