using Communication.Shared.Messages;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Tasks;

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
    }
}
