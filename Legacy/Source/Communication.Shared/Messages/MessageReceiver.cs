namespace Communication.Shared.Messages;

public abstract class MessageReceiver : IMessageReceiver
{
    protected readonly IMessageConverter _messageConverter;
    protected IMessageHandler _messageHandler;

    public MessageReceiver(IMessageConverter messageConverter, IMessageHandler messageHandler)
    {
        _messageConverter = messageConverter ?? throw new ArgumentNullException(nameof(messageConverter));
        _messageHandler = messageHandler;
    }

    public virtual void OnDetectedDisconnection()
    {
        _messageHandler.OnDetectedDisconnection();
    }
}