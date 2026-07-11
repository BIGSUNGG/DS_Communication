using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using LiteNetLib;

namespace Communication.Network.RUDP.Shared.Sessions
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

        protected override bool IsTransportConnected()
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

namespace Communication.Shared.Sessions
{
    [Obsolete("Use Communication.Network.RUDP.Shared.Sessions.RUDPSession instead.")]
    public abstract class RUDPSession : Communication.Network.RUDP.Shared.Sessions.RUDPSession
    {
        protected RUDPSession(NetPeer netPeer, NetManager netManager, Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
            : base(netPeer, netManager, receiverCreater, senderCreater)
        {
        }
    }
}
