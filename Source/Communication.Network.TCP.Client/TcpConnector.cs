using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;

namespace Communication.Network.TCP;

/// <summary>
/// TCP 클라이언트 연결. 연결만 열고, 세션 생성은 앱이 한다.
/// 성공 후 <see cref="Channel"/>을 노출한다.
/// <c>TcpTransportOptions.Tls</c> 설정 시 연결 후 TLS 핸드셰이크까지 완료한 뒤 채널을 노출한다.
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
                    _ = connectTask.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
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

        StreamByteChannel channel;
        if (options?.Tls is { } tls)
        {
            // TLS 핸드셰이크 — 실패(인증서 거부·프로토콜 위반·상한 초과)는 연결 실패(false)로 확정한다.
            SslStream ssl = new(client.GetStream(), leaveInnerStreamOpen: false, tls.RemoteCertificateValidation);
            try
            {
                Task handshake = ssl.AuthenticateAsClientAsync(
                    tls.TargetHost ?? host, clientCertificates: null, enabledSslProtocols: SslProtocols.None, checkCertificateRevocation: false);
                if (!await TlsHandshake.AwaitAsync(ssl, handshake, tls.HandshakeTimeout).ConfigureAwait(false))
                {
                    throw new TimeoutException($"TLS 핸드셰이크가 {tls.HandshakeTimeout}ms 안에 완료되지 않았습니다.");
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // 취소 등록부가 클라이언트를 닫아 핸드셰이크가 실패한 경우 — 취소로 보고한다.
                client.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception)
            {
                try
                {
                    ssl.Dispose();
                }
                catch
                {
                    // 닫기 실패가 정리를 막으면 안 된다.
                }

                client.Dispose();
                return false;
            }

            channel = new StreamByteChannel(ssl, client.Client);
        }
        else
        {
            channel = new StreamByteChannel(client);
        }

        // 소켓 옵션은 원본 소켓에 적용한다(TLS 스트림 아래 공유 소켓).
        channel.Socket.NoDelay = options?.NoDelay ?? true;
        KeepAliveApplicator.Apply(channel.Socket, options?.KeepAlive);
        Channel = channel;
        return true;
    }
}
