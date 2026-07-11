using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using TCP_IOCP_Chat.Shared.Messages;
using System;

namespace TCP_IOCP_Chat.Client;

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

