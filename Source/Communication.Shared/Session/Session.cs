using DS.Communication.Shared.Messages;
using DS.Communication.Shared.Messages.Handler;
using DS.Communication.Shared.Messages.Receiver;
using DS.Communication.Shared.Messages.Sender;
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

namespace DS.Communication.Shared.Session
{
    public abstract class Session : ISession, IDisposable
    {
        TcpClient _tcpClient { get; set; }
        IMessageReceiver _messageReceiver { get; set; }
        IMessageSender _messageSender { get; set; }
        protected bool _disposed;
        private bool _disconnectedNotified;

        public Session(TcpClient tcpClient, Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
        {
            _tcpClient = tcpClient;
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

        public virtual void Disconnect()
        {
            if (!_disconnectedNotified)
            {
                _disconnectedNotified = true;
                OnDisconnected();
            }

            try
            {
                if (_tcpClient.Connected)
                    {
                        try
                        {
                            _tcpClient.Client?.Shutdown(SocketShutdown.Both);
                        }
                        catch
                        {
                        }
                    }

                _tcpClient.Close();
            }
            catch
            {
            }

            Dispose();
        }

        protected abstract void OnDisconnected();

        public void Dispose()
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
