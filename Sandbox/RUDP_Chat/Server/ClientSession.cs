using Communication.Shared.Messages;
using Communication.Shared.Session;
using LiteNetLib;
using System;

namespace RUDP_Chat.Server;

public sealed class ClientSession : RUDPSession
{
    public int SessionId { get; set; }
    public string ClientName { get; set; } = string.Empty;

    public ClientSession(int sessionId, NetPeer netPeer, NetManager netManager, Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
        : base(netPeer, netManager, receiverCreater, senderCreater)
    {
        SessionId = sessionId;
        ClientName = $"Client_{sessionId}";
    }

    protected override void OnDisconnected()
    {
        Console.WriteLine($"클라이언트 {ClientName} 연결 종료");
    }
}
