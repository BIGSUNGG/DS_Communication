using MemoryPack;

namespace DS.Network.Shared.Messages;

[MemoryPackable]
[MemoryPackUnion(0, typeof(C_LoginRequestMessage))]
[MemoryPackUnion(1, typeof(S_LoginResponseMessage))]
[MemoryPackUnion(2, typeof(C_RegisterRequestMessage))]
[MemoryPackUnion(3, typeof(S_RegisterResponseMessage))]
[MemoryPackUnion(4, typeof(C_ChatSendMessage))]
[MemoryPackUnion(5, typeof(S_ChatNotifyMessage))]
public abstract partial class Message
{
}

[MemoryPackable]
public partial class C_LoginRequestMessage : Message
{
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    [MemoryPackConstructor]
    private C_LoginRequestMessage() { }
    
    public C_LoginRequestMessage(string userId, string password)
    {
        UserId = userId;
        Password = password;
    }
}

[MemoryPackable]
public partial class S_LoginResponseMessage : Message
{
    public bool IsSuccessful { get; set; }
    public string Message { get; set; } = string.Empty;

    [MemoryPackConstructor]
    private S_LoginResponseMessage() { }
    
    public S_LoginResponseMessage(bool isSuccessful, string message = "")
    {
        IsSuccessful = isSuccessful;
        Message = message;
    }
}

[MemoryPackable]
public partial class C_RegisterRequestMessage : Message
{
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    [MemoryPackConstructor]
    private C_RegisterRequestMessage() { }
    
    public C_RegisterRequestMessage(string userId, string password)
    {
        UserId = userId;
        Password = password;
    }
}

[MemoryPackable]
public partial class S_RegisterResponseMessage : Message
{
    public bool IsSuccessful { get; set; }
    public string Message { get; set; } = string.Empty;

    [MemoryPackConstructor]
    private S_RegisterResponseMessage() { }
    
    public S_RegisterResponseMessage(bool isSuccessful, string message = "")
    {
        IsSuccessful = isSuccessful;
        Message = message;
    }
}

[MemoryPackable]
public partial class C_ChatSendMessage : Message
{
    public string Content { get; set; } = string.Empty;

    [MemoryPackConstructor]
    private C_ChatSendMessage() { }
    
    public C_ChatSendMessage(string content)
    {
        Content = content;
    }
}

[MemoryPackable]
public partial class S_ChatNotifyMessage : Message
{
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    [MemoryPackConstructor]
    private S_ChatNotifyMessage() { }

    public S_ChatNotifyMessage(string senderName, string content)
    {
        SenderName = senderName;
        Content = content;
    }
}
