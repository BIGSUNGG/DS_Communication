using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using Communication.Shared.Channels;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;

namespace Communication.Tests;

/// <summary>
/// 읽기는 공급된 바이트를 1바이트씩 돌려주고(부분 읽기 검증), 쓰기는 기록만 하는 인메모리 채널.
/// </summary>
internal sealed class FakeByteChannel : IByteChannel
{
    private readonly ConcurrentQueue<byte> _readQueue = new();
    private readonly SemaphoreSlim _readAvailable = new(0);
    private readonly object _writeLock = new();
    private readonly List<byte[]> _writes = new();
    private readonly TaskCompletionSource<bool> _writeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<bool>? _writeGate;
    private volatile bool _connected = true;

    public bool IsConnected => _connected;

    public IReadOnlyList<byte[]> Writes
    {
        get
        {
            lock (_writeLock)
            {
                return _writes.ToList();
            }
        }
    }

    public void SetConnected(bool value) => _connected = value;

    /// <summary>첫 쓰기가 채널에 진입하면 완료되는 신호.</summary>
    public Task WriteEntered => _writeEntered.Task;

    /// <summary>쓰기가 기록된 직후 호출되는 훅(테스트용).</summary>
    public Action? OnWrite { get; set; }

    /// <summary>읽힐 바이트를 공급한다.</summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            _readQueue.Enqueue(b);
            _readAvailable.Release();
        }
    }

    /// <summary>스트림 끝(원격 닫힘)을 알린다. 대기 중인 읽기를 세마포어로 풀어 0 반환으로 이끈다.</summary>
    public void Complete() => _readAvailable.Release(1024);

    /// <summary>이후 쓰기를 게이트가 풀릴 때까지 막는다.</summary>
    public void BlockWrites()
    {
        lock (_writeLock)
        {
            _writeGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void ReleaseWrites()
    {
        TaskCompletionSource<bool>? gate;
        lock (_writeLock)
        {
            gate = _writeGate;
            _writeGate = null;
        }

        gate?.TrySetResult(true);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _readAvailable.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (_readQueue.TryDequeue(out byte value))
        {
            buffer.Span[0] = value;
            return 1;
        }

        return 0; // Complete()로 풀린 경우 — 스트림 끝
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _writeEntered.TrySetResult(true);

        TaskCompletionSource<bool>? gate;
        lock (_writeLock)
        {
            gate = _writeGate;
        }

        if (gate != null)
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (_writeLock)
        {
            _writes.Add(buffer.ToArray());
        }

        OnWrite?.Invoke();
    }

    public void Dispose() => _connected = false;
}

/// <summary>문자열 ↔ UTF8 변환기.</summary>
internal sealed class StringConverter : IMessageConverter
{
    public void Serialize(object message, IBufferWriter<byte> writer)
    {
        byte[] bytes = Encoding.UTF8.GetBytes((string)message);
        writer.Write(bytes);
    }

    public object Deserialize(ReadOnlySpan<byte> message) => Encoding.UTF8.GetString(message);
}

/// <summary>지정한 메시지에서만 직렬화 예외를 던지는 변환기(격리 경로 검증용).</summary>
internal sealed class SelectiveThrowingConverter : IMessageConverter
{
    private readonly string _throwOn;

    public SelectiveThrowingConverter(string throwOn) => _throwOn = throwOn;

    public void Serialize(object message, IBufferWriter<byte> writer)
    {
        if (message is string text && text == _throwOn)
        {
            throw new InvalidOperationException("serialize exploded");
        }

        writer.Write(Encoding.UTF8.GetBytes((string)message));
    }

    public object Deserialize(ReadOnlySpan<byte> message) => Encoding.UTF8.GetString(message);
}

/// <summary>지정한 본문에서만 역직렬화 예외를 던지는 변환기(수신 단절 경로 검증용).</summary>
internal sealed class SelectiveThrowingDeserializer : IMessageConverter
{
    private readonly string _throwOn;

    public SelectiveThrowingDeserializer(string throwOn) => _throwOn = throwOn;

    public void Serialize(object message, IBufferWriter<byte> writer)
        => writer.Write(Encoding.UTF8.GetBytes((string)message));

    public object Deserialize(ReadOnlySpan<byte> message)
    {
        string text = Encoding.UTF8.GetString(message);
        if (text == _throwOn)
        {
            throw new InvalidOperationException("deserialize exploded");
        }

        return text;
    }
}

/// <summary>아무것도 쓰지 않는 변환기(빈 페이로드 거부 검증용).</summary>
internal sealed class EmptyConverter : IMessageConverter
{
    public void Serialize(object message, IBufferWriter<byte> writer)
    {
    }

    public object Deserialize(ReadOnlySpan<byte> message) => Encoding.UTF8.GetString(message);
}

/// <summary>송신 옵션 전달 검증용 파생 옵션.</summary>
internal sealed class TestSendOptions : SendOptions
{
}

/// <summary>
/// 메시지 단위 채널 페이크. 송신 페이로드·옵션을 기록하고, 수신을 테스트에서 발생시킨다.
/// </summary>
internal sealed class FakeMessageChannel : IMessageChannel
{
    private readonly object _lock = new();
    private readonly List<(byte[] Payload, SendOptions? Options)> _sent = new();
    private volatile bool _connected = true;
    private volatile Exception? _sendFailure;

    public bool IsConnected => _connected;

    public event Action<ReadOnlyMemory<byte>>? MessageReceived;

    /// <summary>기록된 송신 (페이로드 사본, 옵션).</summary>
    public IReadOnlyList<(byte[] Payload, SendOptions? Options)> Sent
    {
        get
        {
            lock (_lock)
            {
                return _sent.ToList();
            }
        }
    }

    public void SetConnected(bool value) => _connected = value;

    /// <summary>이후 송신을 지정한 예외로 실패시킨다.</summary>
    public void FailSend(Exception failure) => _sendFailure = failure;

    /// <summary>원격 수신처럼 <see cref="MessageReceived"/>를 발생시킨다.</summary>
    public void RaiseReceived(ReadOnlyMemory<byte> payload) => MessageReceived?.Invoke(payload);

    /// <summary>송신이 기록된 직후 호출되는 훅(테스트용).</summary>
    public Action? OnSend { get; set; }

    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        Exception? failure = _sendFailure;
        if (failure != null)
        {
            return ValueTask.FromException(failure);
        }

        lock (_lock)
        {
            _sent.Add((payload.ToArray(), options));
        }

        OnSend?.Invoke();
        return default;
    }

    public void Dispose() => _connected = false;
}

/// <summary>수신 메시지를 기록하는 핸들러. 지정한 메시지에서는 예외를 던진다.</summary>
internal sealed class RecordingHandler : IMessageHandler
{
    private readonly string? _throwOn;
    private readonly List<object> _messages = new();
    private readonly object _lock = new();

    public RecordingHandler(string? throwOn = null) => _throwOn = throwOn;

    public IReadOnlyList<object> Messages
    {
        get
        {
            lock (_lock)
            {
                return _messages.ToList();
            }
        }
    }

    public void HandleMessage(object message)
    {
        lock (_lock)
        {
            _messages.Add(message);
        }

        if (message is string text && text == _throwOn)
        {
            throw new InvalidOperationException("handler exploded");
        }
    }
}

/// <summary>테스트용 최소 세션.</summary>
internal sealed class TestSession : Session
{
    public TestSession(IByteChannel channel, IMessageConverter converter, IMessageHandler handler, MessageQueueOptions? options = null)
        : base(channel)
    {
        AttachPipeline(new MessagePipeline(channel, converter, handler, options));
    }
}

/// <summary>파이프라인을 붙이지 않는 세션(미부착 송신 계약 검증용).</summary>
internal sealed class UnattachedTestSession : Session
{
    public UnattachedTestSession(IByteChannel channel)
        : base(channel)
    {
    }
}
