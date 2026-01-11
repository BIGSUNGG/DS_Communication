using Communication.Shared.Messages;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Communication.Shared.Session
{
    public abstract class TCPSession : Session
    {
        TcpClient _tcpClient { get; set; }

        public TCPSession(TcpClient tcpClient, Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
            : base(receiverCreater, senderCreater)
        {
            _tcpClient = tcpClient;
        }

        public override bool IsConnected()
        {
            return _tcpClient.Connected;
        }

        protected override void OnDisconnected()
        {
            _tcpClient.GetStream().Socket.Shutdown(SocketShutdown.Both);
            _tcpClient.Close();
        }

        public override void Dispose()
        {
            base.Dispose();
            _tcpClient?.Dispose();
        }
    }
}
