using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;

namespace Communication.Network.TCP;

/// <summary>
/// TCP 수락 루프. 수락된 채널은 <see cref="Accepted"/>로 전달하며, 세션 생성은 앱이 한다.
/// </summary>
public sealed class TcpListener : IDisposable
{
    private readonly IPAddress _address;
    private readonly int _port;
    private System.Net.Sockets.TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public TcpListener(IPAddress address, int port)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _port = port;
    }

    /// <summary>수락된 채널 통지. 세션 생성 등 앱 로직에서 던진 예외는 격리된다.</summary>
    public event Action<IByteChannel>? Accepted;

    /// <summary>바인딩된 실제 엔드포인트. 포트 0(임시 포트) 수락 시 테스트·등록에 사용한다.</summary>
    public EndPoint? LocalEndpoint => _listener?.LocalEndpoint;

    public void Start(TcpTransportOptions? options = null)
    {
        if (_listener != null)
        {
            throw new InvalidOperationException("이미 시작된 리스너입니다.");
        }

        System.Net.Sockets.TcpListener listener = new(_address, _port);
        listener.Start();
        _listener = listener;
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(listener, _cts.Token, options);
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _listener?.Stop();
        }
        catch
        {
        }
        finally
        {
            _listener = null;
        }
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(System.Net.Sockets.TcpListener listener, CancellationToken token, TcpTransportOptions? options)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                // 일시적 수락 오류 — 핫 루프 방지 후 재시도.
                try
                {
                    await Task.Delay(50, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            StreamByteChannel channel = new(client);
            channel.Socket.NoDelay = options?.NoDelay ?? true;
            KeepAliveApplicator.Apply(channel.Socket, options?.KeepAlive);

            // 수락마다 최신 구독자를 읽는다 — Start 이후 구독자도 채널을 받는다.
            Action<IByteChannel>? accepted = Accepted;
            if (accepted is null)
            {
                channel.Dispose(); // 구독자 없음 — 연결이 새지 않도록 정리 후 수락 계속.
                continue;
            }

            try
            {
                accepted.Invoke(channel);
            }
            catch (Exception e)
            {
                Trace.TraceError($"수락 핸들러 예외 — 채널 정리 후 수락 계속: {e}");
                channel.Dispose();
            }
        }
    }
}
