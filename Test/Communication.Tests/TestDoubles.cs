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
        TaskCompletionSource<bool>? gate;
        lock (_writeLock)
        {
            gate = _writeGate;
        }

        if (gate != null)
        {
            await gate.Task.ConfigureAwait(false);
        }

        lock (_writeLock)
        {
            _writes.Add(buffer.ToArray());
        }
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
