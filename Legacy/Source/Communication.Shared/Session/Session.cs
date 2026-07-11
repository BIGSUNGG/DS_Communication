using Communication.Shared.Messages;

namespace Communication.Shared.Sessions
{
    public abstract class Session : ISession, IDisposable
    {
        protected bool _disposed;
        private int _locallyConnected = 1;
        IMessageReceiver _messageReceiver { get; set; }
        IMessageSender _messageSender { get; set; }

        public Session(Func<Session, IMessageReceiver> receiverCreater, Func<Session, IMessageSender> senderCreater)
        {
            _messageReceiver = receiverCreater.Invoke(this);
            _messageSender = senderCreater.Invoke(this);
        }

        /// <summary>
        /// Receiver 끊김 감지 시 호출. transport Connected와 AND 되어 IsConnected에 반영됩니다.
        /// </summary>
        public void MarkDisconnected()
        {
            Interlocked.Exchange(ref _locallyConnected, 0);
        }

        protected bool IsLocallyConnected => Volatile.Read(ref _locallyConnected) == 1;

        public async Task SendAsync(object message, object context)
        {
            if (_messageSender != null)
            {
                await _messageSender.SendAsync(message, context).ConfigureAwait(false);
            }
        }

        public async Task SendAsync(object message)
        {
            if (_messageSender != null)
            {
                await _messageSender.SendAsync(message).ConfigureAwait(false);
            }
        }

        public async Task SendAndFlushAsync(object message, object? context = null, CancellationToken cancellationToken = default)
        {
            if (_messageSender != null)
            {
                await _messageSender.SendAndFlushAsync(message, context, cancellationToken).ConfigureAwait(false);
            }
        }

        public bool IsConnected() => IsLocallyConnected && IsTransportConnected();

        protected abstract bool IsTransportConnected();

        public void Disconnect()
        {
            bool transportWasConnected = IsTransportConnected();
            MarkDisconnected();
            if (transportWasConnected)
            {
                OnDisconnected();
            }

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
            MarkDisconnected();

            if (_messageReceiver is IDisposable receiverDisposable)
                receiverDisposable.Dispose();
            if (_messageSender is IDisposable senderDisposable)
                senderDisposable.Dispose();
        }
    }
}
