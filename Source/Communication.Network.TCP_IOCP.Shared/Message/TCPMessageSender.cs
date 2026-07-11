using Communication.Shared.Messages;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;

namespace Communication.Network.TCP_IOCP.Shared.Messages;

public sealed class TCPMessageSender : MessageSender, IDisposable
{
    private const int MaxCoalesceBytes = 64 * 1024;

    private readonly Socket _socket;
    private bool _disposed;
    private readonly ConcurrentQueue<(byte[] Payload, TaskCompletionSource<bool>? FlushTcs)> _messageQueue = new();
    private readonly SemaphoreSlim _backpressure;
    private readonly CancellationTokenSource _cancellationTokenSource;

    private readonly SocketAsyncEventArgs _sendEventArgs;
    private readonly byte[] _sendBuffer = new byte[MaxCoalesceBytes];
    private int _bufferOffset;
    private int _bytesToSend;
    private int _sending; // 0 = idle, 1 = sending
    private List<TaskCompletionSource<bool>?>? _pendingFlushTcs;
    private byte[]? _rentedLargeBuffer;

    public TCPMessageSender(IMessageConverter messageConverter, Socket socket, MessageQueueOptions? options = null)
        : base(messageConverter)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        options ??= new MessageQueueOptions();
        _backpressure = new SemaphoreSlim(options.MaxPendingMessages, options.MaxPendingMessages);
        _cancellationTokenSource = new CancellationTokenSource();

        _sendEventArgs = new SocketAsyncEventArgs();
        _sendEventArgs.Completed += OnSendCompleted;
    }

    public override async Task SendAsync(object message, object context)
    {
        await SendAsync(message).ConfigureAwait(false);
    }

    public override Task SendAsync(object message)
    {
        return EnqueueAsync(message, flush: false, CancellationToken.None);
    }

    public override Task SendAndFlushAsync(object message, object? context = null, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(message, flush: true, cancellationToken);
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
            StartSend();
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

    private void StartSend()
    {
        if (_disposed || _cancellationTokenSource.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _sending, 0);
            return;
        }

        ReturnRentedLargeBuffer();

        List<(byte[] Payload, TaskCompletionSource<bool>? FlushTcs)> batch = new();
        int totalBytes = 0;

        while (_messageQueue.TryPeek(out var next))
        {
            int needed = 4 + next.Payload.Length;
            if (batch.Count > 0 && totalBytes + needed > MaxCoalesceBytes)
            {
                break;
            }

            // 단일 메시지가 고정 버퍼보다 크면 배치에 하나만 넣고 ArrayPool 경로로 처리
            if (batch.Count == 0 && needed > MaxCoalesceBytes)
            {
                if (!_messageQueue.TryDequeue(out next))
                {
                    break;
                }

                ReleaseBackpressure();
                batch.Add(next);
                totalBytes = needed;
                break;
            }

            if (needed > MaxCoalesceBytes)
            {
                break;
            }

            if (!_messageQueue.TryDequeue(out next))
            {
                break;
            }

            ReleaseBackpressure();
            batch.Add(next);
            totalBytes += needed;

            if (next.FlushTcs != null)
            {
                break;
            }
        }

        if (batch.Count == 0)
        {
            Interlocked.Exchange(ref _sending, 0);
            if (!_messageQueue.IsEmpty && Interlocked.CompareExchange(ref _sending, 1, 0) == 0)
            {
                StartSend();
            }
            return;
        }

        _pendingFlushTcs = new List<TaskCompletionSource<bool>?>(batch.Count);
        foreach (var item in batch)
        {
            _pendingFlushTcs.Add(item.FlushTcs);
        }

        _bufferOffset = 0;
        _bytesToSend = totalBytes;

        if (totalBytes <= _sendBuffer.Length)
        {
            int offset = 0;
            foreach (var item in batch)
            {
                BitConverter.TryWriteBytes(_sendBuffer.AsSpan(offset, 4), item.Payload.Length);
                offset += 4;
                item.Payload.AsSpan().CopyTo(_sendBuffer.AsSpan(offset));
                offset += item.Payload.Length;
            }

            _sendEventArgs.SetBuffer(_sendBuffer, 0, totalBytes);
            _sendEventArgs.UserToken = null;
        }
        else
        {
            byte[] largeBuffer = ArrayPool<byte>.Shared.Rent(totalBytes);
            _rentedLargeBuffer = largeBuffer;
            int offset = 0;
            foreach (var item in batch)
            {
                BitConverter.TryWriteBytes(largeBuffer.AsSpan(offset, 4), item.Payload.Length);
                offset += 4;
                item.Payload.AsSpan().CopyTo(largeBuffer.AsSpan(offset));
                offset += item.Payload.Length;
            }

            _sendEventArgs.SetBuffer(largeBuffer, 0, totalBytes);
            _sendEventArgs.UserToken = largeBuffer;
        }

        if (!_socket.SendAsync(_sendEventArgs))
        {
            ProcessSend(_sendEventArgs);
        }
    }

    private void ReleaseBackpressure()
    {
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
    }

    private void ReturnRentedLargeBuffer()
    {
        if (_rentedLargeBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(_rentedLargeBuffer);
            _rentedLargeBuffer = null;
        }
    }

    private void CompletePendingFlush(bool success, Exception? error = null)
    {
        if (_pendingFlushTcs == null)
        {
            return;
        }

        foreach (var tcs in _pendingFlushTcs)
        {
            if (tcs == null)
            {
                continue;
            }

            if (success)
            {
                tcs.TrySetResult(true);
            }
            else if (error != null)
            {
                tcs.TrySetException(error);
            }
            else
            {
                tcs.TrySetCanceled();
            }
        }

        _pendingFlushTcs = null;
    }

    private void OnSendCompleted(object? sender, SocketAsyncEventArgs e)
    {
        ProcessSend(e);
    }

    private void ProcessSend(SocketAsyncEventArgs e)
    {
        if (_disposed || _cancellationTokenSource.IsCancellationRequested)
        {
            CompletePendingFlush(false);
            ReturnRentedLargeBuffer();
            return;
        }

        try
        {
            if (e.SocketError != SocketError.Success)
            {
                if (e.SocketError == SocketError.OperationAborted)
                {
                    CompletePendingFlush(false);
                    ReturnRentedLargeBuffer();
                    return;
                }

                Trace.WriteLine($"Error sending: {e.SocketError}");
                CompletePendingFlush(false, new SocketException((int)e.SocketError));
                ReturnRentedLargeBuffer();
                Interlocked.Exchange(ref _sending, 0);
                return;
            }

            _bufferOffset += e.BytesTransferred;
            _bytesToSend -= e.BytesTransferred;

            if (_bytesToSend > 0)
            {
                if (e.UserToken is byte[] largeBuffer)
                {
                    e.SetBuffer(largeBuffer, _bufferOffset, _bytesToSend);
                }
                else
                {
                    e.SetBuffer(_sendBuffer, _bufferOffset, _bytesToSend);
                }

                if (!_socket.SendAsync(e))
                {
                    ProcessSend(e);
                }
                return;
            }

            CompletePendingFlush(true);
            ReturnRentedLargeBuffer();
            e.UserToken = null;
            StartSend();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error processing send: {ex.Message}");
            CompletePendingFlush(false, ex);
            ReturnRentedLargeBuffer();
            Interlocked.Exchange(ref _sending, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellationTokenSource.Cancel();

        while (_messageQueue.TryDequeue(out var item))
        {
            ReleaseBackpressure();
            item.FlushTcs?.TrySetCanceled();
        }

        CompletePendingFlush(false);
        ReturnRentedLargeBuffer();

        _cancellationTokenSource.Dispose();
        _backpressure.Dispose();

        _sendEventArgs.Completed -= OnSendCompleted;
        _sendEventArgs.Dispose();
    }
}
