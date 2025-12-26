using Common.Messages.Handler;
using Common.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Messages.Handler
{
    public abstract class MessageHandler : IMessageHandler
    {
        protected ISession _session;
        protected Dictionary<Type, Action<Message>> _handlers = new Dictionary<Type, Action<Message>>();
        object _lock = new object();

        public MessageHandler(ISession session)
        {
            _session = session;
            RegisterHandler();
        }

        protected abstract void RegisterHandler();

        public void HandleMessage(Message message)
        {
            lock (_lock)
            {
                if (_handlers.TryGetValue(message.GetType(), out var handler))
                {
                    handler(message);
                }
                else
                {
                    throw new InvalidOperationException($"No handler registered for message type {message.GetType().Name}");
                }
            }        
        }

        public virtual void OnDetectedDisconnection()
        {
            _session.Disconnect();
        }
    }
}
