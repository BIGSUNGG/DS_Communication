using Communication.Shared.Messages;
using LiteNetLib;
using System.Buffers;
using System.Diagnostics;

namespace Communication.Network.RUDP.Shared.Messages
{
    public sealed class RUDPMessageReceiver : MessageReceiver, IDisposable
    {
        private readonly NetPeer _netPeer;
        private readonly NetManager _netManager;
        private readonly EventBasedNetListener _listener;
        private readonly RUDPNetworkReceiveDispatcher? _receiveDispatcher;
        private bool _disposed;

        /// <param name="receiveDispatcher">서버 등 다중 세션: 한 리스너당 하나의 디스패처로 등록. 단일 클라이언트 연결만이면 null (리스너에 수신기 하나만 붙는 경우).</param>
        public RUDPMessageReceiver(IMessageConverter messageConverter, NetPeer netPeer, NetManager netManager, EventBasedNetListener listener, IMessageHandler messageHandler, RUDPNetworkReceiveDispatcher? receiveDispatcher = null)
            : base(messageConverter, messageHandler)
        {
            _netPeer = netPeer;
            _netManager = netManager;
            _listener = listener;
            _receiveDispatcher = receiveDispatcher;

            if (_receiveDispatcher != null)
            {
                _receiveDispatcher.RegisterReceiver(_netPeer, this);
            }
            else
            {
                _listener.NetworkReceiveEvent += OnNetworkReceiveSinglePeer;
            }

            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        }

        /// <summary>디스패처가 단일 수신 이벤트에서 호출합니다.</summary>
        internal void DispatchFromNetwork(NetPacketReader reader)
        {
            ProcessPacketReader(reader);
        }

        private void OnNetworkReceiveSinglePeer(NetPeer fromPeer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (fromPeer != _netPeer)
            {
                return;
            }

            ProcessPacketReader(reader);
        }

        private void ProcessPacketReader(NetPacketReader reader)
        {
            try
            {
                if (reader.AvailableBytes == 0)
                {
                    reader.Recycle();
                    return;
                }

                int length = reader.AvailableBytes;
                byte[] messageBytes = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    reader.GetBytes(messageBytes, 0, length);
                    reader.Recycle();

                    var message = _messageConverter.Deserialize(messageBytes.AsSpan(0, length));
                    _messageHandler.HandleMessage(message);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(messageBytes);
                }
            }
            catch (Exception ex)
            {
                reader.Recycle();
                Trace.WriteLine($"Error handling received data: {ex}");
                OnDetectedDisconnection();
            }
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (peer == _netPeer)
            {
                OnDetectedDisconnection();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_receiveDispatcher != null)
            {
                _receiveDispatcher.UnregisterReceiver(_netPeer);
            }
            else
            {
                _listener.NetworkReceiveEvent -= OnNetworkReceiveSinglePeer;
            }

            _listener.PeerDisconnectedEvent -= OnPeerDisconnected;
        }
    }
}
