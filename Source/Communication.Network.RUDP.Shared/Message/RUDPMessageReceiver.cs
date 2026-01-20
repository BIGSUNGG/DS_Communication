using Communication.Shared.Messages;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Threading.Tasks;

namespace Communication.Network.RUDP.Shared.Messages
{
    public sealed class RUDPMessageReceiver : MessageReceiver, IDisposable
    {
        private readonly NetPeer _netPeer;
        private readonly NetManager _netManager;
        private readonly EventBasedNetListener _listener;
        private bool _disposed;

        public RUDPMessageReceiver(IMessageConverter messageConverter, NetPeer netPeer, NetManager netManager, EventBasedNetListener listener, IMessageHandler messageHandler)
            : base(messageConverter, messageHandler)
        {
            _netPeer = netPeer;            
            _netManager = netManager;
            _listener = listener;

            // 메시지 수신 이벤트 구독
            _listener.NetworkReceiveEvent += OnNetworkReceive;
            
            // 연결 종료 이벤트 구독
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        }

        private void OnNetworkReceive(NetPeer fromPeer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            // 이 세션의 peer가 아니면 무시
            if (fromPeer != _netPeer)
            {
                reader.Recycle();
                return;
            }

            try
            {
                if (reader.AvailableBytes == 0)
                {
                    reader.Recycle();
                    return;
                }

                byte[] messageBytes = new byte[reader.AvailableBytes];
                reader.GetBytes(messageBytes, 0, messageBytes.Length);
                reader.Recycle();

                var message = _messageConverter.Deserialize(messageBytes);
                _messageHandler.HandleMessage(message);
            }
            catch (Exception ex)
            {
                reader.Recycle();
                Console.WriteLine($"Error handling received data: {ex.Message}");
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
            
            // 이벤트 구독 해제
            _listener.NetworkReceiveEvent -= OnNetworkReceive;
            _listener.PeerDisconnectedEvent -= OnPeerDisconnected;
        }
    }
}
