using MessageProtocol;

namespace TCP_IOCP_Chat.Shared.Messages;

[MessageGroupRoot(0)]
public abstract partial class Message
{
}

[MessageGroupElement(1)]
public partial class C_ChatSendMessage : Message
{
    public string Content { get; set; } = string.Empty;

    private C_ChatSendMessage() { }
    
    public C_ChatSendMessage(string content)
    {
        Content = content;
    }
}

[MessageGroupElement(2)]
public partial class S_ChatNotifyMessage : Message
{
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    private S_ChatNotifyMessage() { }

    public S_ChatNotifyMessage(string senderName, string content)
    {
        SenderName = senderName;
        Content = content;
    }
}

