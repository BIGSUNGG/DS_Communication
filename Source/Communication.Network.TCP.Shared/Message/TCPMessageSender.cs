using Communication.Shared.Messages;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Communication.TCP.Shared.Messages
{
    public sealed class TCPMessageSender : MessageSender, IDisposable
    {
        private bool _disposed;

        private readonly NetworkStream _stream;

        private readonly SemaphoreSlim _signal = new(0, 1);
        private int _signalPending;
        private readonly ConcurrentQueue<byte[]> _messageQueue = new();
        private Task _processMessageQueueTask;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public TCPMessageSender(IMessageConverter messageConverter, NetworkStream stream)
            : base(messageConverter)
        {
            _stream = stream;
            _cancellationTokenSource = new CancellationTokenSource();
            _processMessageQueueTask = Task.Run(() => ProcessMessageQueueLoopAsync(_cancellationTokenSource.Token));
        }

        public override async Task SendAsync(object message, object context)
        {
            await SendAsync(message).ConfigureAwait(false);
        }

        public override Task SendAsync(object message)
        {
            byte[] serializedMessage = _messageConverter.Serialize(message);
            _messageQueue.Enqueue(serializedMessage);
            Signal();
            return Task.CompletedTask;
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
                    while (_messageQueue.TryDequeue(out byte[] messageBytes))
                    {
                        try
                        {
                            int totalLength = 4 + messageBytes.Length;
                            byte[] buffer = ArrayPool<byte>.Shared.Rent(totalLength);
                            try
                            {
                                BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), messageBytes.Length);
                                Buffer.BlockCopy(messageBytes, 0, buffer, 4, messageBytes.Length);
                                await _stream.WriteAsync(buffer, 0, totalLength, cancellationToken).ConfigureAwait(false);
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(buffer);
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
                    Console.WriteLine($"Error sending message: {ex.Message}");
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
        }
    }
}
