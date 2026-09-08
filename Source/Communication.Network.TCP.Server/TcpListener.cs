using System;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;

namespace Communication.Network.TCP;

/// <summary>
/// TCP 수락 루프. 수락된 채널은 <see cref="Accepted"/>로 전달하며, 세션 생성은 앱이 한다.
/// <c>TcpTransportOptions.MaxConnections</c> 설정 시 상한 도달 후 수락된 연결은 즉시 닫는다.
/// <c>TcpTransportOptions.Tls</c>의 <see cref="TcpTlsOptions.ServerCertificate"/> 설정 시
/// 모든 수락 연결에 대해 TLS 핸드셰이크를 먼저 완료한 뒤 채널을 전달한다.
/// </summary>
public sealed class TcpListener : IDisposable
{
    private readonly IPAddress _address;
    private readonly int _port;
    private System.Net.Sockets.TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _connectionCount; // 수락 후 미처분 채널 수 — 상한 강제 기준

    public TcpListener(IPAddress address, int port)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _port = port;
    }

    /// <summary>수락된 채널 통지. 세션 생성 등 앱 로직에서 던진 예외는 격리된다.</summary>
    public event Action<IByteChannel>? Accepted;

    /// <summary>바인딩된 실제 엔드포인트. 포트 0(임시 포트) 수락 시 테스트·등록에 사용한다.</summary>
    public EndPoint? LocalEndpoint => _listener?.LocalEndpoint;

    /// <summary>수락됐으나 아직 Dispose되지 않은 채널 수. <c>MaxConnections</c> 상한 강제의 기준이다.</summary>
    public int ActiveConnectionCount => Volatile.Read(ref _connectionCount);

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

        // 취소된 CTS를 정리한다 — Cancel은 토큰 상태를 고정시키므로 수락 루프의 모든 탈출 경로가
        // 취소 토큰으로 끝난다(취소 후 새 등록 경로 없음). dispose·null로 중지 후에도
        // 리스너가 CTS를 계속 보유하지 않게 한다(재시작 시 새 CTS, 기존 재시작 핀 테스트로 보호).
        try
        {
            _cts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        _cts = null;

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
            catch (Exception e)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                // 일시적 수락 오류 — 핫 루프 방지 후 재시도. 지속 실패는 조용한 수락 중단이
                // 되므로 진단을 남긴다(RUDP 폴링 루프의 예외 기록과 동일한 관례).
                Trace.TraceError($"TCP 수락 예외 — 50ms 후 재시도: {e}");
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

            // 소켓 옵션은 래핑 전에 원본 소켓에 적용한다. 수용 직후 상대가 RST로 끊는 경합으로
            // 적용이 실패하면 이 연결만 버린다 — 이 예외가 루프를 벗어나면 수용이 조용히 죽어
            // 서버 전체가 연결을 받지 못한다(전면 장애). KeepAliveApplicator는 내부에서 이미 격리된다.
            try
            {
                client.NoDelay = options?.NoDelay ?? true;
                KeepAliveApplicator.Apply(client.Client, options?.KeepAlive);
            }
            catch (Exception e)
            {
                Trace.TraceError($"수용 연결 소켓 옵션 적용 실패 — 연결 닫고 수용 계속: {e}");
                try
                {
                    client.Dispose();
                }
                catch
                {
                    // 닫기 실패가 수용 루프를 막으면 안 된다.
                }

                continue;
            }

            // 연결 수 상한 — 초과면 카운트를 되돌리고 즉시 닫은 뒤 수락 계속.
            int active = Interlocked.Increment(ref _connectionCount);
            if (options?.MaxConnections is { } max && active > max)
            {
                Interlocked.Decrement(ref _connectionCount);
                try
                {
                    client.Dispose();
                }
                catch
                {
                    // 닫기 실패가 수락 루프를 막으면 안 된다.
                }

                continue;
            }

            // TLS 경로 — 핸드셰이크는 수락 루프를 점유하지 않는 연결별 태스크로 돌린다.
            // 침묵 클라이언트의 슬로로리스 핸드셰이크가 이후 수락을 지연시키지 못하게 한다.
            // 상한 슬롯은 이미 예약됐다 — 핸드셰이크 실패 시 이 태스크가, 성공 시 채널 Dispose가 회수한다.
            if (options?.Tls?.ServerCertificate is { } certificate)
            {
                _ = HandshakeTlsAsync(client, certificate, options.Tls, token);
                continue;
            }

            // Dispose 시 상한 슬롯 회수 — 세션이 채널(또는 세션 자신을) 정리하면 수에서 빠진다.
            StreamByteChannel channel = new(client, () => Interlocked.Decrement(ref _connectionCount));
            HandOff(channel);
        }
    }

    /// <summary>TLS 핸드셰이크를 완료하고 성공 시에만 채널을 전달한다. 실패(프로토콜 위반·상한 초과)는 로그·정리 후 조용히 끝난다.</summary>
    private async Task HandshakeTlsAsync(TcpClient client, X509Certificate certificate, TcpTlsOptions tls, CancellationToken listenerToken)
    {
        SslStream? ssl = null;
        try
        {
            ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            Task handshake = ssl.AuthenticateAsServerAsync(
                certificate, clientCertificateRequired: false, enabledSslProtocols: SslProtocols.None, checkCertificateRevocation: false);
            if (!await TlsHandshake.AwaitAsync(ssl, handshake, tls.HandshakeTimeout).ConfigureAwait(false))
            {
                throw new TimeoutException($"TLS 핸드셰이크가 {tls.HandshakeTimeout}ms 안에 완료되지 않았습니다.");
            }
        }
        catch (Exception e)
        {
            Trace.TraceError($"TLS 핸드셰이크 실패 — 연결 닫고 수락 계속: {e}");
            try
            {
                ssl?.Dispose(); // 스트림 닫기는 내부 NetworkStream→소켓까지 닫는다. ssl 생성 전 실패(null)면 아래서 클라이언트를 닫는다.
            }
            catch
            {
                // 닫기 실패가 정리를 막으면 안 된다.
            }

            if (ssl is null)
            {
                try
                {
                    client.Dispose(); // 스트림 생성 전 실패 — 클라이언트 직접 정리.
                }
                catch
                {
                    // 닫기 실패가 정리를 막으면 안 된다.
                }
            }

            Interlocked.Decrement(ref _connectionCount); // 수락 루프가 예약한 슬롯 회수
            return;
        }

        // 리스너 정지(Stop) 이후에 완료된 핸드셰이크 — 정지된 리스너에서 Accepted를 늦게
        // 발화하지 않고 폐기한다(슬롯 회수 동일). 클라이언트는 핸드셰이크를 마칠 수 있지만
        // 연결은 즉시 닫힌다. 검사→발화 사이 나노초 창구는 통상 클래스의 잔여 경쟁이다.
        if (listenerToken.IsCancellationRequested)
        {
            try
            {
                ssl.Dispose();
            }
            catch
            {
                // 닫기 실패가 정리를 막으면 안 된다.
            }

            Interlocked.Decrement(ref _connectionCount);
            return;
        }

        HandOff(new StreamByteChannel(ssl, client.Client, () => Interlocked.Decrement(ref _connectionCount)));
    }

    /// <summary>채널을 <see cref="Accepted"/>로 전달한다. 구독자 부재·구독자 예외는 채널 정리로 격리한다.</summary>
    private void HandOff(StreamByteChannel channel)
    {
        // 수락마다 최신 구독자를 읽는다 — Start 이후 구독자도 채널을 받는다.
        Action<IByteChannel>? accepted = Accepted;
        if (accepted is null)
        {
            channel.Dispose(); // 구독자 없음 — 연결이 새지 않도록 정리 후 수락 계속.
            return;
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
