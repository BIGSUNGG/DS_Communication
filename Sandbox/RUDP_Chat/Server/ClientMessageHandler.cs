using Communication.Shared.Messages;
using Communication.Shared.Session;
using RUDP_Chat.Shared.Messages;
using System;

namespace RUDP_Chat.Server;

public sealed class ClientMessageHandler : MessageHandler
{
    public ClientMessageHandler(ISession session)
        : base(session)
    {
    }

    protected override void RegisterMessageType()
    {
        _messageHandleActions.Add(typeof(C_ChatSendMessage), HandleChatSendMessage);
    }

    private void HandleChatSendMessage(object message)
    {
        C_ChatSendMessage chatMessage = (C_ChatSendMessage)message;
        ClientSession? client = _session as ClientSession;

        if (client == null)
            return;

        Console.WriteLine($"[{client.ClientName}] {chatMessage.Content}");

        // 모든 클라이언트에 채팅 전송
        S_ChatNotifyMessage newChatMessage = new S_ChatNotifyMessage(client.ClientName, chatMessage.Content);
        foreach (var s in ClientSessionManager.Instance.Sessions)
        {
            _ = s.SendAsync(newChatMessage);
        }
    }
}
