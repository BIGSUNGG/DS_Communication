using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using System.Net.Sockets;

namespace Communication.Network.TCP.Shared.Sessions;

/// <summary>Canonical namespace alias. Prefer this over <c>Communication.TCP.Shared.Sessions</c>.</summary>
public abstract class TCPSession : Communication.TCP.Shared.Sessions.TCPSession
{
    protected TCPSession(TcpClient tcpClient, Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
        : base(tcpClient, receiverCreater, senderCreater)
    {
    }
}
