using Communication.Shared.Messages;
using Communication.Shared.Session;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Server;

public sealed class ClientSession : TCPSession
{
    public int SessionId { get; set; }
    public int? AccountId { get; set; }
    public string? AccountName { get; set; }

    public ClientSession(int sessionId, TcpClient tcpClient, Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
        : base(tcpClient, receiverCreater, senderCreater)
    {
        SessionId = sessionId;
    }

    protected override void OnDisconnected()
    {
        Console.WriteLine($"클라이언트 {AccountName} 연결 종료");
    }
}

