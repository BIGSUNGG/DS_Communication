using Communication.Shared.Messages;
using Communication.Shared.Session;
using RUDP_Chat.Shared.Messages;
using System;

namespace RUDP_Chat.Client;

public sealed class ClientMessageHandler : MessageHandler
{
    public ClientMessageHandler(ISession session)
        : base(session)
    {
    }

    protected override void RegisterMessageType()
    {
        _messageHandleActions.Add(typeof(S_ChatNotifyMessage), HandleChatNotifyMessage);
    }

    private void HandleChatNotifyMessage(object message)
    {
        S_ChatNotifyMessage chatMessage = (S_ChatNotifyMessage)message;
        Console.WriteLine($"{chatMessage.SenderName}: {chatMessage.Content}");
    }
}
