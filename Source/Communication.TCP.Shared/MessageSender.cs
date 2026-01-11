using Communication.Shared.Messages;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Communication.Shared.Messages
{
    public sealed class TCPMessageSender : MessageSender, IDisposable
    {      
        private bool _disposed;

        private readonly NetworkStream _stream;    

        private readonly SemaphoreSlim _sendLock = new(0, 1);
        private readonly ConcurrentQueue<byte[]> _messageQueue = new();
        private Task _processMessageQueueTask;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public TCPMessageSender(NetworkStream stream)
        {
            _stream = stream;
            _cancellationTokenSource = new CancellationTokenSource();
            _processMessageQueueTask = Task.Run(() => ProcessMessageQueueLoopAsync(_cancellationTokenSource));
        }
        
        public override async Task SendAsync(object message)
        {
            byte[] serializedMessage = MessageConverter.Instance.Serialize(message);
            _messageQueue.Enqueue(serializedMessage);
            _sendLock.Release();
        }

        private async Task ProcessMessageQueueLoopAsync(CancellationTokenSource cancellationTokenSource)
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                await _sendLock.WaitAsync();
                try
                {
                    while (_messageQueue.TryDequeue(out byte[] messageBytes))
                    {
                        try
                        {
                            await _stream.WriteAsync(BitConverter.GetBytes(messageBytes.Length), 0, 4);
                            await _stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                            await _stream.FlushAsync();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error sending message: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending message: {ex.Message}");
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
            _cancellationTokenSource.Dispose();
            _processMessageQueueTask.Wait(TimeSpan.FromSeconds(1));
            _sendLock.Dispose();
        }
    }
}
