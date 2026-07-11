using Communication.Shared.Messages;

namespace Communication.Shared.Messages
{
    public abstract class MessageSender : IMessageSender
    {
        protected readonly IMessageConverter _messageConverter;

        public MessageSender(IMessageConverter messageConverter)
        {
            _messageConverter = messageConverter ?? throw new ArgumentNullException(nameof(messageConverter));
        }

        public abstract Task SendAsync(object message);
        public abstract Task SendAsync(object message, object context);
        public abstract Task SendAndFlushAsync(object message, object? context = null, CancellationToken cancellationToken = default);
    }
}
