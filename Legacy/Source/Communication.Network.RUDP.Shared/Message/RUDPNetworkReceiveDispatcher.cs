using LiteNetLib;
using System.Collections.Concurrent;

namespace Communication.Network.RUDP.Shared.Messages;

/// <summary>
/// 하나의 <see cref="EventBasedNetListener"/>에 대해 <see cref="NetworkReceiveEvent"/>를 한 번만 구독하고,
/// <see cref="NetPeer"/>별로 <see cref="RUDPMessageReceiver"/>에 전달합니다.
/// 여러 수신기가 각각 이벤트에 붙으면 peer 불일치 시 잘못 <c>Recycle</c>되어 다른 세션 패킷이 사라지는 문제가 생깁니다.
/// </summary>
public sealed class RUDPNetworkReceiveDispatcher : IDisposable
{
    private readonly EventBasedNetListener _listener;
    private readonly ConcurrentDictionary<NetPeer, RUDPMessageReceiver> _receivers = new();
    private bool _disposed;

    public RUDPNetworkReceiveDispatcher(EventBasedNetListener listener)
    {
        _listener = listener;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
    }

    private void OnNetworkReceive(NetPeer fromPeer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (_receivers.TryGetValue(fromPeer, out var receiver))
        {
            receiver.DispatchFromNetwork(reader);
        }
        else
        {
            reader.Recycle();
        }
    }

    internal void RegisterReceiver(NetPeer peer, RUDPMessageReceiver receiver)
    {
        _receivers[peer] = receiver;
    }

    internal void UnregisterReceiver(NetPeer peer)
    {
        _receivers.TryRemove(peer, out _);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listener.NetworkReceiveEvent -= OnNetworkReceive;
    }
}
