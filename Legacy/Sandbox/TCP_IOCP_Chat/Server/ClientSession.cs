using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using Communication.Network.TCP_IOCP.Shared.Sessions;
using Communication.Network.TCP_IOCP.Shared.Messages;
using TCP_IOCP_Chat.Shared.Messages;
using System;
using System.Net.Sockets;

namespace TCP_IOCP_Chat.Server;

public sealed class ClientSession : TCPSession
{
    public int SessionId { get; set; }
    public string ClientName { get; set; } = string.Empty;

    public ClientSession(
        int sessionId,
        Socket socket,
        Func<Session, IMessageReceiver> receiverCreater,
        Func<Session, IMessageSender> senderCreater)
        : base(socket, receiverCreater, senderCreater)
    {
        SessionId = sessionId;
        ClientName = $"Client_{sessionId}";

    }

    protected override void OnDisconnected()
    {
        Console.WriteLine($"클라이언트 {ClientName} 연결 종료");
        ClientSessionManager.Instance.RemoveSession(this);
    }
}

