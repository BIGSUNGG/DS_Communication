using Communication.Shared.Messages;
using MessageProtocol;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Communication.Shared.Session
{
    public abstract class Session : ISession, IDisposable
    {
        protected bool _disposed;
        IMessageReceiver _messageReceiver { get; set; }
        IMessageSender _messageSender { get; set; }

        public Session(Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
        {
            _messageReceiver = receiverCreater.Invoke(this);
            _messageSender = senderCreater.Invoke(this);
        }

        public async Task SendAsync(object message)
        {
            if (_messageSender != null)
            {
                await _messageSender.SendAsync(message);
            }
        }

        public abstract bool IsConnected();

        public void Disconnect()
        {
            if (IsConnected())
                OnDisconnected();

            Dispose();
        }

        protected abstract void OnDisconnected();

        public virtual void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            
            if (_messageReceiver is IDisposable receiverDisposable)
                receiverDisposable.Dispose();
            if (_messageSender is IDisposable senderDisposable)
                senderDisposable.Dispose();
        }
    }
}
