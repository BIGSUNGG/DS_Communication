using ClientGUI;
using DS.Communication.Shared.Messages;
using DS.Communication.Shared.Messages.Handler;
using DS.Communication.Shared.Session;
using Message = DS.Communication.Shared.Messages.Message;

namespace Client;

internal sealed class ServerMessageHandler : MessageHandler
{
    public ServerMessageHandler(ISession session)
        : base(session)
    {
    }

    protected override void RegisterHandler()
    {
        _handlers.Add(typeof(S_LoginResponseMessage), HandleLoginResponse);
        _handlers.Add(typeof(S_RegisterResponseMessage), HandleRegisterResponse);
        _handlers.Add(typeof(S_ChatNotifyMessage), HandleChatMessage);
    }

    void HandleLoginResponse(Message message)
    {
        var loginResponse = (S_LoginResponseMessage)message;
        UniqueForm.FindInstance<LoginForm>()?.OnLoginResponse(loginResponse);
    }

    void HandleRegisterResponse(Message message)
    {
        var registerResponse = (S_RegisterResponseMessage)message;
        UniqueForm.FindInstance<LoginForm>()?.OnRegisterResponse(registerResponse);
    }

    void HandleChatMessage(Message message)
    {
        var chatMessage = (S_ChatNotifyMessage)message;
        UniqueForm.FindInstance<ChatForm>()?.OnReceiveChatMessage(chatMessage);
    }
}

