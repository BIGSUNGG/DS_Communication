using DS.Communication.Shared.Messages.Handler;
using DS.Communication.Shared.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DS.Communication.Shared.Messages.Handler
{
    public abstract class MessageHandler : IMessageHandler
    {
        protected ISession _session;
        protected Dictionary<Type, Action<object>> _handlers = new Dictionary<Type, Action<object>>();
        object _lock = new object();

        public MessageHandler(ISession session)
        {
            _session = session;
            RegisterHandler();
        }

        protected abstract void RegisterHandler();

        public void HandleMessage(object message)
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
