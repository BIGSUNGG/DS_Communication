using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using System.Net.Sockets;

namespace Communication.TCP.Shared.Sessions
{
    public abstract class TCPSession : Session
    {
        TcpClient _tcpClient { get; set; }

        public TCPSession(TcpClient tcpClient, Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
            : base(receiverCreater, senderCreater)
        {
            _tcpClient = tcpClient;
        }

        protected override bool IsTransportConnected()
        {
            return _tcpClient.Connected;
        }

        protected override void OnDisconnected()
        {
            try
            {
                _tcpClient.Client.Shutdown(SocketShutdown.Both);
            }
            catch { }

            _tcpClient.Close();
        }

        public override void Dispose()
        {
            base.Dispose();
            _tcpClient?.Dispose();
        }
    }
}
