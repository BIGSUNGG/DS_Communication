using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using LiteNetLib;
using System;

namespace RUDP_Chat.Client;

public sealed class ClientSession : RUDPSession
{
    public ClientSession(NetPeer netPeer, NetManager netManager, Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
        : base(netPeer, netManager, receiverCreater, senderCreater)
    {
    }

    protected override void OnDisconnected()
    {
        Console.WriteLine("서버와의 연결이 종료되었습니다.");
    }
}
