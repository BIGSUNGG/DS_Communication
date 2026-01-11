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
        public abstract Task SendAsync(object message);
        public abstract Task SendAsync(object message, object context);
    }
}
