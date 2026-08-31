using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;
using Communication.Shared.Connection;
using Communication.Shared.Framing;
using Communication.Shared.Threading;

namespace Communication.Shared.Messages;

/// <summary>
/// 메시지 ↔ 와이어 공통 경로. 송신 큐·백프레셔·직렬화·(프레이밍)·수신·디스패치를 담당한다.
/// Connect/Accept·재접속·하트비트는 다루지 않는다. 채널 정리는 소유자(세션)가 한다.
/// </summary>
public sealed class MessagePipeline : IDisposable
{
    private readonly IByteChannel? _byteChannel;
    private readonly IMessageChannel? _messageChannel;
    private readonly IMessageConverter _converter;
    private readonly IMessageHandler _handler;
    private readonly MessageQueueOptions _options;
    private readonly SemaphoreSlim _sendSlots;
    private readonly SemaphoreSlim _receiveSlots;
    private readonly ConcurrentQueue<PendingSend> _sendQueue = new();
    private readonly ConcurrentQueue<object> _receiveQueue = new();
    private readonly SignalGate _sendGate = new();
    private readonly SignalGate _dispatchGate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly LengthPrefixFrameReader? _frameReader;
    private readonly List<PendingSend> _batch = new();
    private int _stopped;              // 0 = 동작, 1 = 정지
    private int _notifiedDisconnect;   // 끊김 통지 1회 보장

    /// <summary>바이트 스트림 채널(길이 프레이밍 + coalesce 송신) 파이프라인.</summary>
    public MessagePipeline(IByteChannel channel, IMessageConverter converter, IMessageHandler handler, MessageQueueOptions? options = null)
    {
        _byteChannel = channel ?? throw new ArgumentNullException(nameof(channel));
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? new MessageQueueOptions();
        _sendSlots = new SemaphoreSlim(_options.MaxPendingMessages, _options.MaxPendingMessages);
        _receiveSlots = new SemaphoreSlim(_options.MaxPendingMessages, _options.MaxPendingMessages);
        _frameReader = new LengthPrefixFrameReader(channel);
    }

    /// <summary>메시지 단위 채널(프레이밍 없음, 예: RUDP) 파이프라인.</summary>
    public MessagePipeline(IMessageChannel channel, IMessageConverter converter, IMessageHandler handler, MessageQueueOptions? options = null)
    {
        _messageChannel = channel ?? throw new ArgumentNullException(nameof(channel));
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? new MessageQueueOptions();
        _sendSlots = new SemaphoreSlim(_options.MaxPendingMessages, _options.MaxPendingMessages);
        _receiveSlots = new SemaphoreSlim(_options.MaxPendingMessages, _options.MaxPendingMessages);
    }

    /// <summary>수신 루프·채널 오류로 끊김이 감지되면 1회 발생. 재접속 신호는 없다.</summary>
    public event Action<DisconnectReason, Exception?>? Disconnected;

    /// <summary>전송 계층 관점의 연결 상태.</summary>
    public bool IsChannelConnected => _byteChannel?.IsConnected ?? _messageChannel!.IsConnected;

    /// <summary>송신·수신 루프를 시작한다. 한 번만 호출한다.</summary>
    public void Start()
    {
        if (_byteChannel != null)
        {
            _ = Task.Run(SendLoopByteAsync);
            _ = Task.Run(ReceiveLoopByteAsync);
        }
        else
        {
            _ = Task.Run(SendLoopMessageAsync);
            _messageChannel!.MessageReceived += OnMessageChannelReceived;
        }

        if (!_options.InlineDispatch)
        {
            _ = Task.Run(DispatchLoopAsync);
        }
    }

    /// <summary>메시지를 송신 큐에 넣는다. 큐가 가득 차면 공간이 날 때까지 비동기 대기한다.</summary>
    public Task SendAsync(object message, SendOptions? options = null) => EnqueueAsync(message, options, null);

    /// <summary>큐잉 후 와이어 기록 완료까지 기다린다.</summary>
    public async Task SendAndFlushAsync(object message, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<bool> flush = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueAsync(message, options, flush).ConfigureAwait(false);

        if (cancellationToken.CanBeCanceled)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() => flush.TrySetCanceled(cancellationToken));
            await flush.Task.ConfigureAwait(false);
        }
        else
        {
            await flush.Task.ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        if (_messageChannel != null)
        {
            _messageChannel.MessageReceived -= OnMessageChannelReceived;
        }

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _sendGate.Dispose();
        _dispatchGate.Dispose();

        // 미송신 잔여 항목의 flush 대기를 정리한다.
        while (_sendQueue.TryDequeue(out PendingSend leftover))
        {
            leftover.Flush?.TrySetException(new InvalidOperationException("파이프라인이 정지되어 송신하지 못했습니다."));
        }

        _cts.Dispose();
    }

    private async Task EnqueueAsync(object message, SendOptions? options, TaskCompletionSource<bool>? flush)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        if (_stopped != 0) throw new InvalidOperationException("파이프라인이 정지되어 송신할 수 없습니다.");

        try
        {
            await _sendSlots.WaitAsync(_cts.Token).ConfigureAwait(false); // 백프레셔: 공간 날 때까지 대기
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("파이프라인이 정지되어 송신할 수 없습니다.");
        }

        if (_stopped != 0)
        {
            _sendSlots.Release();
            throw new InvalidOperationException("파이프라인이 정지되어 송신할 수 없습니다.");
        }

        _sendQueue.Enqueue(new PendingSend(message, options, flush));
        _sendGate.Signal();
    }

    private async Task SendLoopByteAsync()
    {
        using PooledBufferWriter writer = new();
        try
        {
            while (true)
            {
                await _sendGate.WaitAsync(_cts.Token).ConfigureAwait(false);
                if (!_sendQueue.IsEmpty)
                {
                    await WriteCoalescedBatchAsync(writer).ConfigureAwait(false);
                }

                _sendGate.ResetPendingAndResignalIf(() => !_sendQueue.IsEmpty);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception e)
        {
            RequestDisconnect(DisconnectReason.Error, e);
        }
        finally
        {
            DrainSendQueueFaulted();
        }
    }

    private async Task WriteCoalescedBatchAsync(PooledBufferWriter writer)
    {
        writer.Clear();
        _batch.Clear();

        while (_sendQueue.TryDequeue(out PendingSend item))
        {
            int frameStart = writer.WrittenCount;
            writer.GetSpan(LengthPrefixFramer.HeaderSize);
            writer.Advance(LengthPrefixFramer.HeaderSize);
            _converter.Serialize(item.Message, writer);
            BinaryPrimitives.WriteInt32LittleEndian(writer.GetWritableSpan().Slice(frameStart), writer.WrittenCount - frameStart - LengthPrefixFramer.HeaderSize);
            _batch.Add(item);

            if (writer.WrittenCount >= _options.CoalesceLimitBytes)
            {
                break;
            }
        }

        try
        {
            await _byteChannel!.WriteAsync(writer.WrittenMemory, _cts.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            foreach (PendingSend item in _batch)
            {
                item.Flush?.TrySetException(e);
            }

            throw;
        }

        foreach (PendingSend item in _batch)
        {
            _sendSlots.Release();
            item.Flush?.TrySetResult(true);
        }
    }

    private async Task SendLoopMessageAsync()
    {
        using PooledBufferWriter writer = new();
        try
        {
            while (true)
            {
                await _sendGate.WaitAsync(_cts.Token).ConfigureAwait(false);

                while (_sendQueue.TryDequeue(out PendingSend item))
                {
                    writer.Clear();
                    _converter.Serialize(item.Message, writer);
                    try
                    {
                        await _messageChannel!.SendAsync(writer.WrittenMemory, item.Options, _cts.Token).ConfigureAwait(false);
                        _sendSlots.Release();
                        item.Flush?.TrySetResult(true);
                    }
                    catch (Exception e)
                    {
                        item.Flush?.TrySetException(e);
                        throw;
                    }
                }

                _sendGate.ResetPendingAndResignalIf(() => !_sendQueue.IsEmpty);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception e)
        {
            RequestDisconnect(DisconnectReason.Error, e);
        }
        finally
        {
            DrainSendQueueFaulted();
        }
    }

    private async Task ReceiveLoopByteAsync()
    {
        try
        {
            while (true)
            {
                int length = await _frameReader!.ReadFrameLengthAsync(_cts.Token).ConfigureAwait(false);
                if (length == 0)
                {
                    RequestDisconnect(DisconnectReason.Remote, null);
                    return;
                }

                byte[] rented = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    await _frameReader.ReadExactAsync(rented.AsMemory(0, length), _cts.Token).ConfigureAwait(false);
                    object message = _converter.Deserialize(rented.AsSpan(0, length));
                    await DispatchReceivedAsync(message).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception e)
        {
            RequestDisconnect(DisconnectReason.Error, e);
        }
    }

    private void OnMessageChannelReceived(ReadOnlyMemory<byte> payload)
    {
        if (_stopped != 0)
        {
            return;
        }

        object message;
        try
        {
            message = _converter.Deserialize(payload.Span);
        }
        catch (Exception e)
        {
            RequestDisconnect(DisconnectReason.Error, e);
            return;
        }

        if (_options.InlineDispatch)
        {
            DispatchInline(message);
        }
        else
        {
            // 수신 콜백 스레드를 막지 않게 백프레셔 대기는 비동기로 넘긴다.
            _ = EnqueueForDispatchAsync(message);
        }
    }

    private ValueTask DispatchReceivedAsync(object message)
    {
        if (_options.InlineDispatch)
        {
            DispatchInline(message);
            return default;
        }

        return EnqueueForDispatchAsync(message);
    }

    private void DispatchInline(object message)
    {
        try
        {
            _handler.HandleMessage(message);
        }
        catch (Exception e)
        {
            Trace.TraceError($"핸들러 예외 — 격리 후 계속: {e}");
        }
    }

    private async ValueTask EnqueueForDispatchAsync(object message)
    {
        try
        {
            await _receiveSlots.WaitAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        _receiveQueue.Enqueue(message);
        _dispatchGate.Signal();
    }

    private async Task DispatchLoopAsync()
    {
        try
        {
            while (true)
            {
                await _dispatchGate.WaitAsync(_cts.Token).ConfigureAwait(false);

                while (_receiveQueue.TryDequeue(out object? message))
                {
                    try
                    {
                        _handler.HandleMessage(message);
                    }
                    catch (Exception e)
                    {
                        Trace.TraceError($"핸들러 예외 — 격리 후 계속: {e}");
                    }
                    finally
                    {
                        _receiveSlots.Release();
                    }
                }

                _dispatchGate.ResetPendingAndResignalIf(() => !_receiveQueue.IsEmpty);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RequestDisconnect(DisconnectReason reason, Exception? exception)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return; // 정리 중 발생한 오류는 통지하지 않는다.
        }

        if (Interlocked.Exchange(ref _notifiedDisconnect, 1) != 0)
        {
            return;
        }

        Disconnected?.Invoke(reason, exception);
    }

    private void DrainSendQueueFaulted()
    {
        InvalidOperationException error = new("파이프라인이 정지되어 송신하지 못했습니다.");
        while (_sendQueue.TryDequeue(out PendingSend leftover))
        {
            leftover.Flush?.TrySetException(error);
        }
    }

    private readonly struct PendingSend
    {
        public PendingSend(object message, SendOptions? options, TaskCompletionSource<bool>? flush)
        {
            Message = message;
            Options = options;
            Flush = flush;
        }

        public object Message { get; }
        public SendOptions? Options { get; }
        public TaskCompletionSource<bool>? Flush { get; }
    }
}
