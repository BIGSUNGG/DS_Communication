using System;
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
    private int _started;              // Start 1회 가드
    private int _stopped;              // 0 = 동작, 1 = 정지
    private int _disposed;             // Dispose 1회 가드 — 끊김 통지가 _stopped를 선점해도 정리는 반드시 수행
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
    /// <exception cref="InvalidOperationException">이미 시작된 파이프라인인 경우.</exception>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("Start은 한 번만 호출할 수 있습니다.");
        }

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
    public Task SendAndFlushAsync(object message, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            // 이미 취소됨 — 큐잉하지 않고 즉시 취소 완료.
            return Task.FromCanceled(cancellationToken);
        }

        return SendAndFlushCoreAsync(message, options, cancellationToken);
    }

    private async Task SendAndFlushCoreAsync(object message, SendOptions? options, CancellationToken cancellationToken)
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _stopped, 1);

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

        // 루프는 ObjectDisposedException을 잡아 조용히 탈출한다.
        _sendSlots.Dispose();
        _receiveSlots.Dispose();
        _frameReader?.Dispose();
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
        catch (ObjectDisposedException)
        {
            throw new InvalidOperationException("파이프라인이 정지되어 송신할 수 없습니다.");
        }

        if (_stopped != 0)
        {
            try
            {
                _sendSlots.Release();
            }
            catch (ObjectDisposedException)
            {
                // 정지 중 해제 — 무시.
            }

            throw new InvalidOperationException("파이프라인이 정지되어 송신할 수 없습니다.");
        }

        _sendQueue.Enqueue(new PendingSend(message, options, flush));
        _sendGate.Signal();

        // Dispose 경쟁: 직전 정지면 Dispose 드레인이 이 항목을 놓쳤을 수 있다.
        // 드레인과 TrySet 계열이라 중복 시 먼저 faults가 남는다.
        if (_stopped != 0)
        {
            flush?.TrySetException(new InvalidOperationException("파이프라인이 정지되어 송신하지 못했습니다."));
        }
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
            try
            {
                writer.GetSpan(LengthPrefixFramer.HeaderSize);
                writer.Advance(LengthPrefixFramer.HeaderSize);
                _converter.Serialize(item.Message, writer);

                // 빈 payload는 상대편에서 EOF와 구분이 안 되므로 송신을 거부한다.
                int payloadLength = writer.WrittenCount - frameStart - LengthPrefixFramer.HeaderSize;
                if (payloadLength <= 0 || payloadLength > LengthPrefixFramer.MaxFrameLength)
                {
                    throw new ArgumentException(
                        $"직렬화 결과 페이로드 길이 {payloadLength}는 0보다 크고 {LengthPrefixFramer.MaxFrameLength} 이하여야 합니다.");
                }

                BinaryPrimitives.WriteInt32LittleEndian(writer.GetWritableSpan().Slice(frameStart), payloadLength);
            }
            catch (Exception e)
            {
                // 직렬화·검증 실패 — 이 항목만 격리(부분 프레임 되감기)하고 나머지는 계속 보낸다.
                writer.RewindTo(frameStart);
                item.Flush?.TrySetException(e);
                ReleaseSlotQuietly();
                Trace.TraceError($"직렬화 실패 — 항목 격리 후 계속: {e}");
                continue;
            }

            _batch.Add(item);
            if (writer.WrittenCount >= _options.CoalesceLimitBytes)
            {
                break;
            }
        }

        if (_batch.Count == 0)
        {
            return; // 전부 직렬화 격리됨 — 전송할 바이트 없음.
        }

        try
        {
            await _byteChannel!.WriteAsync(writer.WrittenMemory, _cts.Token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            foreach (PendingSend pending in _batch)
            {
                pending.Flush?.TrySetException(e);
            }

            throw;
        }

        // Flush를 먼저 완료하고 슬롯을 해제한다 — Dispose가 그 사이에 _sendSlots를 정리해도 호출자 완료는 보장된다.
        foreach (PendingSend pending in _batch)
        {
            pending.Flush?.TrySetResult(true);
        }

        _sendSlots.Release(_batch.Count); // 배치 단위 1회 해제
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

                    try
                    {
                        _converter.Serialize(item.Message, writer);
                        if (writer.WrittenCount == 0)
                        {
                            // 빈 payload는 송신하지 않는다(메시지 채널은 길이 상한 없음).
                            throw new ArgumentException("직렬화 결과 페이로드가 비어 있습니다.");
                        }
                    }
                    catch (Exception e)
                    {
                        // 직렬화 실패 — 이 항목만 격리하고 송신은 계속한다.
                        item.Flush?.TrySetException(e);
                        ReleaseSlotQuietly();
                        Trace.TraceError($"직렬화 실패 — 항목 격리 후 계속: {e}");
                        continue;
                    }

                    try
                    {
                        await _messageChannel!.SendAsync(writer.WrittenMemory, item.Options, _cts.Token).ConfigureAwait(false);
                        item.Flush?.TrySetResult(true); // Flush 먼저 완료 — Dispose 경쟁으로 슬롯 해제가 무산돼도 호출자 완료는 보장
                        _sendSlots.Release();
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
                ReadOnlyMemory<byte> frame = await _frameReader!.ReadFrameAsync(_cts.Token).ConfigureAwait(false);
                if (frame.IsEmpty)
                {
                    RequestDisconnect(DisconnectReason.Remote, null);
                    return;
                }

                object message = _converter.Deserialize(frame.Span);
                await DispatchReceivedAsync(message).ConfigureAwait(false);
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

                int count = 0;
                while (_receiveQueue.TryDequeue(out object? message))
                {
                    count++;
                    try
                    {
                        _handler.HandleMessage(message);
                    }
                    catch (Exception e)
                    {
                        Trace.TraceError($"핸들러 예외 — 격리 후 계속: {e}");
                    }
                }

                if (count > 0)
                {
                    _receiveSlots.Release(count); // 드레인 단위 1회 해제
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
        finally
        {
            // 끊김·정지 직전에 큐에 들어온 메시지는 탈출 전까지 전달한다.
            int leftover = 0;
            while (_receiveQueue.TryDequeue(out object? message))
            {
                leftover++;
                try
                {
                    _handler.HandleMessage(message);
                }
                catch (Exception e)
                {
                    Trace.TraceError($"핸들러 예외 — 격리 후 계속: {e}");
                }
            }

            if (leftover > 0)
            {
                try
                {
                    _receiveSlots.Release(leftover);
                }
                catch (ObjectDisposedException)
                {
                }
            }
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

        // 통지 뒤에 정지 — 단독 파이프라인도 이후 송신이 fault 되고 루프가 탈출한다.
        // 순서 주의: 먼저 정지하면 구독자가 다시 이 경로를 타거나 정리가 통지를 막을 수 있다.
        Volatile.Write(ref _stopped, 1);
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ReleaseSlotQuietly()
    {
        try
        {
            _sendSlots.Release();
        }
        catch (ObjectDisposedException)
        {
            // 정지 중 해제 — 무시.
        }
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
