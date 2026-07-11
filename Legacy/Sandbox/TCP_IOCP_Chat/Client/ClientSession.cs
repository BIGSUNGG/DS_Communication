using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using Communication.Network.TCP_IOCP.Shared.Sessions;
using Communication.Network.TCP_IOCP.Shared.Messages;
using System.Net.Sockets;
using System;

namespace TCP_IOCP_Chat.Client;

public sealed class ClientSession : TCPSession
{
    public ClientSession(
        Socket socket,
        Func<Session, IMessageReceiver> receiverCreater,
        Func<Session, IMessageSender> senderCreater)
        : base(socket, receiverCreater, senderCreater)
    {
    }

    protected override void OnDisconnected()
    {
        Console.WriteLine("서버와의 연결이 종료되었습니다.");
    }
}

