using Communication.Shared.Messages;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;

namespace Communication.TCP.Shared.Messages
{
    public class TCPMessageSender : MessageSender, IDisposable
    {
        private const int MaxCoalesceBytes = 64 * 1024;

        private bool _disposed;
        private readonly NetworkStream _stream;
        private readonly ConcurrentQueue<(byte[] Payload, TaskCompletionSource<bool>? FlushTcs)> _messageQueue = new();
        private readonly SemaphoreSlim _backpressure;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private int _sending; // 0 = idle, 1 = sending

        public TCPMessageSender(IMessageConverter messageConverter, NetworkStream stream, MessageQueueOptions? options = null)
            : base(messageConverter)
        {
            _stream = stream;
            options ??= new MessageQueueOptions();
            _backpressure = new SemaphoreSlim(options.MaxPendingMessages, options.MaxPendingMessages);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public override async Task SendAsync(object message, object context)
        {
            await SendAsync(message).ConfigureAwait(false);
        }

        public override async Task SendAsync(object message)
        {
            await EnqueueAsync(message, flush: false, CancellationToken.None).ConfigureAwait(false);
        }

        public override async Task SendAndFlushAsync(object message, object? context = null, CancellationToken cancellationToken = default)
        {
            await EnqueueAsync(message, flush: true, cancellationToken).ConfigureAwait(false);
        }

        private async Task EnqueueAsync(object message, bool flush, CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TCPMessageSender));
            }

            await _backpressure.WaitAsync(cancellationToken).ConfigureAwait(false);

            byte[] serializedMessage = _messageConverter.Serialize(message);
            TaskCompletionSource<bool>? flushTcs = flush
                ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                : null;

            _messageQueue.Enqueue((serializedMessage, flushTcs));

            if (Interlocked.CompareExchange(ref _sending, 1, 0) == 0)
            {
                _ = StartSendAsync();
            }

            if (flushTcs != null)
            {
                using (cancellationToken.Register(static state =>
                {
                    ((TaskCompletionSource<bool>)state!).TrySetCanceled();
                }, flushTcs))
                {
                    await flushTcs.Task.ConfigureAwait(false);
                }
            }
        }

        private async Task StartSendAsync()
        {
            try
            {
                while (!_disposed && !_cancellationTokenSource.IsCancellationRequested)
                {
                    if (!_messageQueue.TryPeek(out _))
                    {
                        Interlocked.Exchange(ref _sending, 0);
                        if (!_messageQueue.IsEmpty && Interlocked.CompareExchange(ref _sending, 1, 0) == 0)
                        {
                            continue;
                        }
                        return;
                    }

                    await SendCoalescedBatchAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error sending message: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _sending, 0);
                if (!_disposed && !_messageQueue.IsEmpty && Interlocked.CompareExchange(ref _sending, 1, 0) == 0)
                {
                    _ = StartSendAsync();
                }
            }
        }

        private async Task SendCoalescedBatchAsync(CancellationToken cancellationToken)
        {
            List<(byte[] Payload, TaskCompletionSource<bool>? FlushTcs)> batch = new();
            int totalBytes = 0;

            while (_messageQueue.TryPeek(out var next))
            {
                int needed = 4 + next.Payload.Length;
                if (batch.Count > 0 && totalBytes + needed > MaxCoalesceBytes)
                {
                    break;
                }

                if (!_messageQueue.TryDequeue(out next))
                {
                    break;
                }

                try
                {
                    _backpressure.Release();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SemaphoreFullException)
                {
                }

                batch.Add(next);
                totalBytes += needed;

                if (next.FlushTcs != null)
                {
                    // flush 경계: 해당 메시지까지 포함해 쓰고 완료한다
                    break;
                }
            }

            if (batch.Count == 0)
            {
                return;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(totalBytes);
            try
            {
                int offset = 0;
                foreach (var item in batch)
                {
                    BitConverter.TryWriteBytes(buffer.AsSpan(offset, 4), item.Payload.Length);
                    offset += 4;
                    Buffer.BlockCopy(item.Payload, 0, buffer, offset, item.Payload.Length);
                    offset += item.Payload.Length;
                }

                await _stream.WriteAsync(buffer.AsMemory(0, totalBytes), cancellationToken).ConfigureAwait(false);

                foreach (var item in batch)
                {
                    item.FlushTcs?.TrySetResult(true);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error sending message: {ex.Message}");
                foreach (var item in batch)
                {
                    item.FlushTcs?.TrySetException(ex);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
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

            while (_messageQueue.TryDequeue(out var item))
            {
                try
                {
                    _backpressure.Release();
                }
                catch
                {
                }

                item.FlushTcs?.TrySetCanceled();
            }

            _cancellationTokenSource.Dispose();
            _backpressure.Dispose();
        }
    }
}
