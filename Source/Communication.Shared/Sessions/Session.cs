using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;
using Communication.Shared.Connection;
using Communication.Shared.Messages;

namespace Communication.Shared.Sessions;

/// <summary>
/// <see cref="MessagePipeline"/>과 채널을 소유하는 세션 기본 구현.
/// 전송 패키지는 이 클래스를 상속해 세션을 만들고, 앱은 그 세션을 채널 위에 직접 생성한다.
/// </summary>
public abstract class Session : ISession
{
    private readonly IDisposable _channel;
    private readonly object _disconnectGate = new();
    private EventHandler<DisconnectedEventArgs>? _disconnectedSubscribers;
    private DisconnectedEventArgs? _finalDisconnectArgs;
    private MessagePipeline? _pipeline;
    private int _disconnected; // 0 = 연결됨, 1 = 끊김(통지 완료 또는 진행 중)

    /// <param name="channel">세션이 소유·정리하는 채널.</param>
    protected Session(IDisposable channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    /// <summary>
    /// 끊김 통지 — 세션당 정확히 1회. **늦은 구독자(이미 끊긴 뒤 구독)에게도 구독 즉시 1회 재생된다**
    /// (구독 타이밍 때문에 끊김을 놓치는 앱 부류를 원천 차단). 구독자 예외는 격리(Trace).
    /// </summary>
    public event EventHandler<DisconnectedEventArgs>? Disconnected
    {
        add
        {
            if (value is null)
            {
                return;
            }

            DisconnectedEventArgs? replay = null;
            lock (_disconnectGate)
            {
                if (Volatile.Read(ref _disconnected) != 0)
                {
                    replay = _finalDisconnectArgs; // 이미 끊김 — 구독자 목록 대신 즉시 재생(중복 없음).
                }
                else
                {
                    _disconnectedSubscribers += value; // 아직 살아있음 — 정상 발화 경로로 1회.
                }
            }

            if (replay is not null)
            {
                InvokeIsolated(value, replay);
            }
        }

        remove
        {
            lock (_disconnectGate)
            {
                _disconnectedSubscribers -= value;
            }
        }
    }

    public bool IsConnected() => Volatile.Read(ref _disconnected) == 0 && (_pipeline?.IsChannelConnected ?? false);

    /// <summary>송신 파이프라인을 연결하고 수신 루프를 시작한다. 파생 생성자에서 한 번만 호출한다.</summary>
    protected void AttachPipeline(MessagePipeline pipeline)
    {
        if (pipeline is null) throw new ArgumentNullException(nameof(pipeline));
        if (_pipeline != null) throw new InvalidOperationException("파이프라인은 한 번만 연결할 수 있습니다.");

        _pipeline = pipeline;
        pipeline.Disconnected += OnPipelineDisconnected;
        pipeline.Start();
    }

    public Task SendAsync(object message) => SendAsync(message, null);

    public Task SendAsync(object message, SendOptions? options)
    {
        MessagePipeline? pipeline = GetLivePipeline();
        if (pipeline is null)
        {
            return Task.FromException(NoLivePipelineException());
        }

        return pipeline.SendAsync(message, options);
    }

    public Task SendAndFlushAsync(object message, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        MessagePipeline? pipeline = GetLivePipeline();
        if (pipeline is null)
        {
            return Task.FromException(NoLivePipelineException());
        }

        return pipeline.SendAndFlushAsync(message, options, cancellationToken);
    }

    public void Disconnect() => MarkDisconnected(DisconnectReason.Local, null);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        MarkDisconnected(DisconnectReason.Local, null);
    }

    /// <summary>
    /// 끊김을 기록하고 파이프라인·채널을 정리한 뒤 <see cref="Disconnected"/>를 1회 발생시킨다.
    /// 중복 호출은 무시된다.
    /// </summary>
    protected void MarkDisconnected(DisconnectReason reason, Exception? exception)
    {
        if (Interlocked.Exchange(ref _disconnected, 1) != 0)
        {
            return;
        }

        DisconnectedEventArgs args = new(reason, exception);
        EventHandler<DisconnectedEventArgs>? subscribers;
        lock (_disconnectGate)
        {
            _finalDisconnectArgs = args; // 늦은 구독자 재생용 — 발화 전에 확정(락 안).
            subscribers = _disconnectedSubscribers;
        }

        try
        {
            _pipeline?.Dispose();
        }
        catch
        {
            // 정리 실패가 통지를 막으면 안 된다.
        }

        try
        {
            _channel.Dispose();
        }
        catch
        {
            // 위와 동일.
        }

        // 발화는 정리 뒤 — 구독자는 이미 정리된 세션 상태를 관측한다(기존 순서 보존).
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate subscriber in subscribers.GetInvocationList())
        {
            InvokeIsolated((EventHandler<DisconnectedEventArgs>)subscriber, args);
        }
    }

    /// <summary>구독자를 하나씩 호출한다 — 예외를 던지는 구독자도 나머지와 격리된다(Trace).</summary>
    private void InvokeIsolated(EventHandler<DisconnectedEventArgs> handler, DisconnectedEventArgs args)
    {
        try
        {
            handler(this, args);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Disconnected 구독자 예외 — 격리 후 계속: {e}");
        }
    }

    private MessagePipeline? GetLivePipeline()
        => Volatile.Read(ref _disconnected) != 0 ? null : _pipeline;

    /// <summary>송신할 파이프라인이 없을 때 반환할 예외 — 끊김·미부착을 구분한다(동기 throw 아님).</summary>
    private InvalidOperationException NoLivePipelineException()
        => Volatile.Read(ref _disconnected) != 0
            ? SendAfterDisconnectException()
            : new InvalidOperationException("파이프라인이 연결되지 않은 세션입니다.");

    private static InvalidOperationException SendAfterDisconnectException()
        => new("세션이 끊겨 송신할 수 없습니다.");

    private void OnPipelineDisconnected(DisconnectReason reason, Exception? exception)
        => MarkDisconnected(reason, exception);
}
