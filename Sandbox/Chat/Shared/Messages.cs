using MessageProtocol;

namespace Communication.Shared.Messages;

[MessageGroupRoot(0)]
public abstract partial class Message
{
}

[MessageGroupElement(1)]
public partial class C_LoginRequestMessage : Message
{
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    private C_LoginRequestMessage() { }
    
    public C_LoginRequestMessage(string userId, string password)
    {
        UserId = userId;
        Password = password;
    }
}

[MessageGroupElement(2)]
public partial class S_LoginResponseMessage : Message
{
    public bool IsSuccessful { get; set; }
    public string Message { get; set; } = string.Empty;

    private S_LoginResponseMessage() { }
    
    public S_LoginResponseMessage(bool isSuccessful, string message = "")
    {
        IsSuccessful = isSuccessful;
        Message = message;
    }
}

[MessageGroupElement(3)]
public partial class C_RegisterRequestMessage : Message
{
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    private C_RegisterRequestMessage() { }
    
    public C_RegisterRequestMessage(string userId, string password)
    {
        UserId = userId;
        Password = password;
    }
}

[MessageGroupElement(4)]
public partial class S_RegisterResponseMessage : Message
{
    public bool IsSuccessful { get; set; }
    public string Message { get; set; } = string.Empty;

    private S_RegisterResponseMessage() { }
    
    public S_RegisterResponseMessage(bool isSuccessful, string message = "")
    {
        IsSuccessful = isSuccessful;
        Message = message;
    }
}

[MessageGroupElement(5)]
public partial class C_ChatSendMessage : Message
{
    public string Content { get; set; } = string.Empty;

    private C_ChatSendMessage() { }
    
    public C_ChatSendMessage(string content)
    {
        Content = content;
    }
}

[MessageGroupElement(6)]
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
