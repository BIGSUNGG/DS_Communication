using System;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;

namespace Communication.Network.RUDP;

/// <summary>
/// RUDP 클라이언트 연결. 연결만 열고, 세션 생성은 앱이 한다. 성공 후 <see cref="Channel"/>을 노출한다.
/// </summary>
/// <remarks>
/// 클라이언트는 peer가 하나뿐이므로 채널이 내부 호스트(폴링 스레드·NetManager)까지 소유한다 —
/// 세션이나 채널을 Dispose하면 남는 자원 없이 정리된다. 들어오는 접속 요청은 거부된다.
/// </remarks>
public sealed class RudpConnector
{
    /// <summary>연결 성공 후 사용 가능한 채널. 실패 시 <c>null</c>.</summary>
    public IMessageChannel? Channel { get; private set; }

    /// <returns>연결 성공 여부. 실패(접속 거부·호스트 해석 불가·재시도 소진) 시 <c>false</c>.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/>이 취소된 경우.</exception>
    public async Task<bool> ConnectAsync(string host, int port, RudpTransportOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (host is null) throw new ArgumentNullException(nameof(host));

        RudpNetHost netHost = new(options);
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        netHost.PeerAccepted = channel =>
        {
            if (Channel is null)
            {
                Channel = channel;
                completion.TrySetResult(true);
                return;
            }

            channel.Dispose(); // 클라이언트는 연결 1개만 — 여분은 정리.
        };
        netHost.PeerFailed = () => completion.TrySetResult(false);

        if (!netHost.StartClient())
        {
            netHost.Dispose();
            return false;
        }

        // 취소는 연결 시도를 중단할 수 없으므로 결과 대기를 취소로 끝내고 호스트를 정리한다(TcpConnector와 동일 방식).
        using CancellationTokenRegistration registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        if (!netHost.RequestConnect(host, port))
        {
            netHost.Dispose();
            return false;
        }

        bool connected;
        try
        {
            connected = await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            netHost.Dispose();
            Channel = null;
            throw new OperationCanceledException(cancellationToken);
        }

        if (!connected)
        {
            netHost.Dispose();
            Channel = null;
            return false;
        }

        return true; // 성공 — 호스트 소유권은 채널이 가진다.
    }
}
