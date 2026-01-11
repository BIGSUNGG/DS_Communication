using Communication.Shared.Messages;
using Communication.Shared.Session;
using System;
using System.Net.Sockets;

namespace Client.Bot;

/// <summary>
/// Bot 전용 세션
/// - 연결 종료 시 콘솔에만 출력
/// </summary>
internal sealed class BotSession : TCPSession
{
    private readonly string _name;

    public BotSession(
        string name,
        TcpClient tcpClient,
        Func<Session, IMessageReceiver> receiverCreator,
        Func<Session, IMessageSender> senderCreator)
        : base(tcpClient, receiverCreator, senderCreator)
    {
        _name = name;
    }

    protected override void OnDisconnected()
    {
        Console.WriteLine($"[Bot:{_name}] Session disconnected.");
    }
}


