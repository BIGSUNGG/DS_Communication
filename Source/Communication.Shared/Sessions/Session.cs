using System;
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
    private MessagePipeline? _pipeline;
    private int _disconnected; // 0 = 연결됨, 1 = 끊김(통지 완료 또는 진행 중)

    /// <param name="channel">세션이 소유·정리하는 채널.</param>
    protected Session(IDisposable channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public event EventHandler<DisconnectedEventArgs>? Disconnected;

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
            return Task.FromException(SendAfterDisconnectException());
        }

        return pipeline.SendAsync(message, options);
    }

    public Task SendAndFlushAsync(object message, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        MessagePipeline? pipeline = GetLivePipeline();
        if (pipeline is null)
        {
            return Task.FromException(SendAfterDisconnectException());
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

        Disconnected?.Invoke(this, new DisconnectedEventArgs(reason, exception));
    }

    private MessagePipeline? GetLivePipeline()
    {
        if (Volatile.Read(ref _disconnected) != 0)
        {
            return null;
        }

        return _pipeline ?? throw new InvalidOperationException("파이프라인이 연결되지 않은 세션입니다.");
    }

    private static InvalidOperationException SendAfterDisconnectException()
        => new("세션이 끊겨 송신할 수 없습니다.");

    private void OnPipelineDisconnected(DisconnectReason reason, Exception? exception)
        => MarkDisconnected(reason, exception);
}
