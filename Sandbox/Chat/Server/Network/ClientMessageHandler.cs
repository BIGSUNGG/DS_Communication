using DS.Communication.Shared.Messages;
using DS.Communication.Shared.Messages.Handler;
using DS.Communication.Shared.Session;
using DB.GateWay;
using System;

namespace Server;

public sealed class ClientMessageHandler : MessageHandler
{
    public ClientMessageHandler(ISession session)
        : base(session)
    {
    }

    protected override void RegisterHandler()
    {
        _handlers.Add(typeof(C_LoginRequestMessage), HandleLoginRequest);
        _handlers.Add(typeof(C_RegisterRequestMessage), HandleRegisterRequest);
        _handlers.Add(typeof(C_ChatSendMessage), HandleChatSendMessage);
    }

    private void HandleLoginRequest(Message message)
    {
        C_LoginRequestMessage loginMessage = (C_LoginRequestMessage)message;
        ClientSession? client = _session as ClientSession;

        if (client == null)
            return;

        Console.WriteLine($"클라이언트 {client.SessionId} 로그인 시도: {loginMessage.UserId}");

        // AccountManager를 통한 로그인 처리 (세션 전달)
        var (success, account, messageText) = AccountManager.Instance.Login(client, loginMessage.UserId, loginMessage.Password);

        if (success && account != null)
        {
            // 로그인 성공
            client.AccountId = account.Id;
            client.AccountName = account.Name;
            Console.WriteLine($"클라이언트 {client.SessionId} 로그인 성공: {account.Name}");
            _ = client.SendAsync(new S_LoginResponseMessage(true, messageText));
        }
        else
        {
            // 로그인 실패
            Console.WriteLine($"클라이언트 {client.SessionId} 로그인 실패: {loginMessage.UserId}");
            _ = client.SendAsync(new S_LoginResponseMessage(false, messageText));
        }
    }

    private void HandleRegisterRequest(Message message)
    {
        C_RegisterRequestMessage registerMessage = (C_RegisterRequestMessage)message;
        ClientSession? client = _session as ClientSession;

        if (client == null)
            return;

        Console.WriteLine($"클라이언트 {client.SessionId} 회원가입 시도: {registerMessage.UserId}");

        // AccountManager를 통한 회원가입 처리
        var (success, messageText) = AccountManager.Instance.Register(registerMessage.UserId, registerMessage.Password);

        if (success)
        {
            Console.WriteLine($"클라이언트 {client.SessionId} 회원가입 성공: {registerMessage.UserId}");
            _ = client.SendAsync(new S_RegisterResponseMessage(true, messageText));
        }
        else
        {
            Console.WriteLine($"클라이언트 {client.SessionId} 회원가입 실패: {messageText}");
            _ = client.SendAsync(new S_RegisterResponseMessage(false, messageText));
        }
    }

    private void HandleChatSendMessage(Message message)
    {
        C_ChatSendMessage chatMessage = (C_ChatSendMessage)message;
        ClientSession? client = _session as ClientSession;

        if (client == null)
            return;

        // 로그인 체크
        if (client.AccountId == null || string.IsNullOrEmpty(client.AccountName))
        {
            Console.WriteLine($"클라이언트 {client.SessionId} 채팅 시도 실패: 로그인되지 않음");
            return;
        }

        Console.WriteLine($"클라이언트 {client.SessionId}({client.AccountName})로부터 채팅 메시지 수신: {chatMessage.Content}");

        // DB에 채팅 저장 - AccountGateWay를 Select로 가져와야 Id를 사용할 수 있음
        AccountGateWay? senderAccount = AccountGateWay.Select(client.AccountId.Value);
        if (senderAccount != null)
        {
            ChatGateWay chatGateWay = new ChatGateWay
            {
                DateTime = DateTime.Now,
                Text = chatMessage.Content,
                Sender = senderAccount
            };
            chatGateWay.Insert();
        }

        // 모든 클라이언트에 채팅 전송
        S_ChatNotifyMessage newChatMessage = new S_ChatNotifyMessage(client.AccountName, chatMessage.Content);
        foreach (var s in ClientSessionManager.Instance.Sessions)
        {
            _ = s.SendAsync(newChatMessage);
        }
    }
}

