using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using System;
using System.Threading.Tasks;
using Message = Communication.Shared.Messages.Message;

namespace Client.Bot;

/// <summary>
/// Bot 전용 메시지 핸들러
/// - GUI 의존성 없이 콘솔/로그로만 동작
/// - 로그인/회원가입/채팅 응답을 TaskCompletionSource 를 통해 BotClient 에 전달
/// </summary>
internal sealed class BotMessageHandler : MessageHandler
{
    private readonly BotClient _botClient;

    public BotMessageHandler(ISession session, BotClient botClient)
        : base(session)
    {
        _botClient = botClient;
    }

    protected override void RegisterMessageType()
    {
        _messageHandleActions.Add(typeof(S_LoginResponseMessage), HandleLoginResponse);
        _messageHandleActions.Add(typeof(S_RegisterResponseMessage), HandleRegisterResponse);
        _messageHandleActions.Add(typeof(S_ChatNotifyMessage), HandleChatNotify);
    }

    private void HandleLoginResponse(object message)
    {
        var res = (S_LoginResponseMessage)message;
        Console.WriteLine($"[Bot:{_botClient.Name}] LoginResponse - Success={res.IsSuccessful}, Message={res.Message}");
        _botClient.CompleteLogin(res);
    }

    private void HandleRegisterResponse(object message)
    {
        var res = (S_RegisterResponseMessage)message;
        Console.WriteLine($"[Bot:{_botClient.Name}] RegisterResponse - Success={res.IsSuccessful}, Message={res.Message}");
        _botClient.CompleteRegister(res);
    }

    private void HandleChatNotify(object message)
    {
        var chat = (S_ChatNotifyMessage)message;
        Console.WriteLine($"[Bot:{_botClient.Name}] ChatNotify - {chat.SenderName}: {chat.Content}");
        _botClient.OnChatNotified(chat);
    }

    public override void OnDetectedDisconnection()
    {
        Console.WriteLine($"[Bot:{_botClient.Name}] Disconnected from server.");
        base.OnDetectedDisconnection();
    }
}


