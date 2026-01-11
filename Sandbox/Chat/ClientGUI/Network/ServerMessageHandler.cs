using ClientGUI;
using Communication.Shared.Messages;
using Communication.Shared.Session;
using Message = Communication.Shared.Messages.Message;

namespace Client;

internal sealed class ServerMessageHandler : MessageHandler
{
    public ServerMessageHandler(ISession session)
        : base(session)
    {
    }

    protected override void RegisterMessageType()
    {
        _messageHandleActions.Add(typeof(S_LoginResponseMessage), HandleLoginResponse);
        _messageHandleActions.Add(typeof(S_RegisterResponseMessage), HandleRegisterResponse);
        _messageHandleActions.Add(typeof(S_ChatNotifyMessage), HandleChatMessage);
    }

    void HandleLoginResponse(object message)
    {
        var loginResponse = (S_LoginResponseMessage)message;
        UniqueForm.FindInstance<LoginForm>()?.OnLoginResponse(loginResponse);
    }

    void HandleRegisterResponse(object message)
    {
        var registerResponse = (S_RegisterResponseMessage)message;
        UniqueForm.FindInstance<LoginForm>()?.OnRegisterResponse(registerResponse);
    }

    void HandleChatMessage(object message)
    {
        var chatMessage = (S_ChatNotifyMessage)message;
        UniqueForm.FindInstance<ChatForm>()?.OnReceiveChatMessage(chatMessage);
    }
}

