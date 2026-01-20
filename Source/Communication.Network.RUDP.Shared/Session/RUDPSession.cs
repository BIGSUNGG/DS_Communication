using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using LiteNetLib;
using System;

namespace Communication.Shared.Sessions
{
    public abstract class RUDPSession : Session
    {
        protected NetPeer? _netPeer { get; set; }
        protected NetManager? _netManager { get; set; }

        public RUDPSession(NetPeer netPeer, NetManager netManager, Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
            : base(receiverCreater, senderCreater)
        {
            _netPeer = netPeer;
            _netManager = netManager;
        }

        public override bool IsConnected()
        {
            return _netPeer != null && _netPeer.ConnectionState == ConnectionState.Connected;
        }

        protected override void OnDisconnected()
        {
            if (_netPeer != null && _netManager != null)
            {
                _netManager.DisconnectPeer(_netPeer);
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            // NetPeer와 NetManager는 외부에서 관리되므로 여기서 dispose하지 않음
        }
    }
}
