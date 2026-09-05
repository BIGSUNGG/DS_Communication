using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;

namespace Communication.Network.TCP;

/// <summary>
/// TCP 클라이언트 연결. 연결만 열고, 세션 생성은 앱이 한다.
/// 성공 후 <see cref="Channel"/>을 노출한다.
/// </summary>
public sealed class TcpConnector
{
    /// <summary>연결 성공 후 사용 가능한 채널. 실패 시 <c>null</c>.</summary>
    public IByteChannel? Channel { get; private set; }

    /// <returns>연결 성공 여부. 취소 시에는 <see cref="OperationCanceledException"/>.</returns>
    public async Task<bool> ConnectAsync(string host, int port, TcpTransportOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TcpClient client = new();
        try
        {
            // netstandard2.1 TcpClient.ConnectAsync는 취소를 직접 지원하지 않아 취소 시 클라이언트를 닫아 끊는다.
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                try
                {
                    client.Dispose();
                }
                catch
                {
                }
            });

            Task connectTask = client.ConnectAsync(host, port);

            // 반개방 호스트(침묵 경로)는 OS SYN 재시도가 수십 초까지 끌 수 있다 — 상한이 먼저
            // 걸리면 연결 실패(false)로 확정하고, 진행 중 연결의 최종 예외는 관찰만 한다(미관찰 방지).
            // 사용자 취소는 위 등록부가 클라이언트를 닫아 connectTask를 즉시 실패시키므로 여기로 오지 않는다.
            if (options?.ConnectTimeout is { } connectTimeoutMs)
            {
                Task timeoutTask = Task.Delay(connectTimeoutMs, CancellationToken.None);
                if (await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false) == timeoutTask)
                {
                    client.Dispose();
                    _ = connectTask.ContinueWith(static _ => { }, TaskScheduler.Default);
                    return false;
                }
            }

            await connectTask.ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception)
        {
            client.Dispose();
            return false;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new OperationCanceledException(cancellationToken);
        }

        StreamByteChannel channel = new(client);
        channel.Socket.NoDelay = options?.NoDelay ?? true;
        KeepAliveApplicator.Apply(channel.Socket, options?.KeepAlive);
        Channel = channel;
        return true;
    }
}
