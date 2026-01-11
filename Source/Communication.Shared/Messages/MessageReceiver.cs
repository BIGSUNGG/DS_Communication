namespace Communication.Shared.Messages;

public class MessageReceiver : IMessageReceiver
{
    protected IMessageHandler _messageHandler;

    public MessageReceiver(IMessageHandler messageHandler)
    {
        _messageHandler = messageHandler;
    }

    public virtual void OnDetectedDisconnection()
    {
        _messageHandler.OnDetectedDisconnection();
    }
}