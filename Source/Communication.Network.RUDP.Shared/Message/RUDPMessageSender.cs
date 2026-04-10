using Communication.Shared.Messages;
using Communication.Network.RUDP.Shared;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Communication.Network.RUDP.Shared.Messages
{
    public sealed class RUDPMessageSender : MessageSender, IDisposable
    {
        private bool _disposed;
        private readonly NetPeer _netPeer;
        private readonly NetDataWriter _dataWriter;
        private readonly SemaphoreSlim _sendLock = new(0, 1);
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

        public override async Task SendAsync(object message, object context)
        {
            DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered;
            
            if (context is MessageSendContext sendContext)
            {
                deliveryMethod = (DeliveryMethod)sendContext.Reliable;
            }

            byte[] serializedMessage = _messageConverter.Serialize(message);
            _messageQueue.Enqueue((serializedMessage, deliveryMethod));
            _sendLock.Release();
            await Task.CompletedTask;
        }

        public override async Task SendAsync(object message)
        {
            await SendAsync(message, new MessageSendContext(ReliableType.ReliableOrdered));
        }

        private async Task ProcessMessageQueueLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _sendLock.WaitAsync(cancellationToken);
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
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing message queue: {ex.Message}");
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
                _processMessageQueueTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch 
            {
            }
            _cancellationTokenSource.Dispose();
            _sendLock.Dispose();
            _dataWriter.Reset();
        }
    }
}
