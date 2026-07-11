using Communication.Shared.Messages;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Concurrent;

namespace Communication.Network.RUDP.Shared.Messages
{
    public sealed class RUDPMessageSender : MessageSender, IDisposable
    {
        private bool _disposed;
        private readonly NetPeer _netPeer;
        private readonly NetDataWriter _dataWriter;
        private readonly SemaphoreSlim _signal = new(0, 1);
        private int _signalPending;
        private readonly ConcurrentQueue<(byte[] data, DeliveryMethod method)> _messageQueue = new();
        private Task _processMessageQueueTask;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public RUDPMessageSender(IMessageConverter messageConverter, NetPeer netPeer)
            : base(messageConverter)
        {
            _netPeer = netPeer;
            _dataWriter = new NetDataWriter();
            _cancellationTokenSource = new CancellationTokenSource();
            _processMessageQueueTask = Task.Run(() => ProcessMessageQueueLoopAsync(_cancellationTokenSource.Token));
        }

        public override Task SendAsync(object message, object context)
        {
            DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered;

            if (context is MessageSendContext sendContext)
            {
                deliveryMethod = (DeliveryMethod)sendContext.Reliable;
            }

            byte[] serializedMessage = _messageConverter.Serialize(message);
            _messageQueue.Enqueue((serializedMessage, deliveryMethod));
            Signal();
            return Task.CompletedTask;
        }

        public override async Task SendAsync(object message)
        {
            await SendAsync(message, new MessageSendContext(ReliableType.ReliableOrdered)).ConfigureAwait(false);
        }

        private void Signal()
        {
            if (Interlocked.CompareExchange(ref _signalPending, 1, 0) == 0)
            {
                try
                {
                    _signal.Release();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SemaphoreFullException)
                {
                }
            }
        }

        private async Task ProcessMessageQueueLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                try
                {
                    while (_messageQueue.TryDequeue(out var messageData))
                    {
                        try
                        {
                            if (_netPeer != null && _netPeer.ConnectionState == ConnectionState.Connected)
                            {
                                _dataWriter.Reset();
                                _dataWriter.Put(messageData.data);
                                _netPeer.Send(_dataWriter, messageData.method);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error sending message: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing message queue: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _signalPending, 0);
                    if (!_messageQueue.IsEmpty)
                    {
                        Signal();
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellationTokenSource.Cancel();
            try
            {
                _signal.Release();
            }
            catch
            {
            }

            try
            {
                _processMessageQueueTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch
            {
            }

            _cancellationTokenSource.Dispose();
            _signal.Dispose();
            _dataWriter.Reset();
        }
    }
}
