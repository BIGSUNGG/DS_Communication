using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Communication.Network.TCP;

/// <summary>
/// TCP 전송 TLS(<see cref="SslStream"/>) 옵션. <see cref="TcpTransportOptions.Tls"/>에 설정하면
/// 연결 확립 후 프레임 통신 전에 TLS 핸드셰이크를 완료한다. 기본(<c>null</c>)은 평문 — 기존 동작 그대로.
/// 서버(리스너)는 <see cref="ServerCertificate"/> 설정 시, 클라이언트(커넥터)는 옵션 자체가 설정된 경우 TLS를 켠다.
/// </summary>
public sealed class TcpTlsOptions
{
    private string? _targetHost;

    /// <summary>기본 핸드셰이크 상한(ms).</summary>
    public const int DefaultHandshakeTimeoutMs = 15_000;

    /// <summary>
    /// 서버 인증서. **리스너에서만 사용** — 설정 시 모든 수락 연결에 대해 핸드셰이크를 먼저 완료한 뒤
    /// <c>Accepted</c>로 전달한다. 핸드셰이크 실패·상한 초과 연결은 즉시 닫히고 수락은 계속된다.
    /// 클라이언트 연결에서는 쓰이지 않는다.
    /// </summary>
    public X509Certificate? ServerCertificate { get; set; }

    /// <summary>
    /// 클라이언트가 검증할 대상 호스트명(SNI·이름 일치 검증). <c>null</c>(기본)이면
    /// <c>TcpConnector.ConnectAsync</c>에 전달한 <c>host</c> 인자를 그대로 쓴다.
    /// IP로 접속하는데 인증서가 호스트명으로 발급된 경우 등에 설정한다. **클라이언트에서만 쓰인다.**
    /// </summary>
    /// <exception cref="ArgumentException">빈 문자열인 경우.</exception>
    public string? TargetHost
    {
        get => _targetHost;
        set
        {
            if (value is not null && value.Length == 0)
            {
                throw new ArgumentException("대상 호스트명은 빈 문자열일 수 없습니다.", nameof(value));
            }

            _targetHost = value;
        }
    }

    /// <summary>
    /// 서버 인증서 검증 콜백(**클라이언트 전용**). <c>null</c>이면 OS 기본 검증(신뢰 체인·이름 일치) —
    /// 자체 서명 인증서는 기본 검증에서 거부된다. 개발 환경에서 자체 서명 인증서를 수용하는 등
    /// 커스텀 정책에 사용한다. <b>프로덕션에서 무조건 통과(<c>=&gt; true</c>)시키는 콜백은 중간자 공격을
    /// 여는 것이므로 금지</b> — 필요한 경우 인증서 지문·신뢰 체인을 정확히 검사한다.
    /// </summary>
    public RemoteCertificateValidationCallback? RemoteCertificateValidation { get; set; }

    /// <summary>
    /// TLS 핸드셰이크 상한(ms). 연결만 열고 핸드셰이크를 끌어안는 슬로로리스 방어 —
    /// 상한 내에 완료되지 않으면 연결을 끊는다(서버: 연결 닫기·슬롯 회수, 클라이언트: 연결 실패 확정).
    /// 기본 <see cref="DefaultHandshakeTimeoutMs"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">0 이하인 경우.</exception>
    public int HandshakeTimeout
    {
        get => _handshakeTimeoutMs;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _handshakeTimeoutMs = value;
        }
    }

    private int _handshakeTimeoutMs = DefaultHandshakeTimeoutMs;
}

/// <summary>
/// 핸드셰이크 상한 대기 공통 경로(서버·클라이언트 커넥터 내부용).
/// netstandard2.1 <see cref="SslStream"/> 인증 API에는 취소 토큰이 없어 시간 경쟁으로 상한을 건다.
/// </summary>
internal static class TlsHandshake
{
    /// <summary>
    /// 핸드셰이크를 상한 안에 기다린다. 상한 초과 시 스트림을 폐기해 진행 중 핸드셰이크를 중단하고
    /// <c>false</c>를 돌려준다(중단으로 핸드셰이크 태스크가 예외 완료하므로 관찰 처리도 여기서).
    /// 핸드셰이크 자체가 실패한 경우 예외는 호출자에게 그대로 전파한다.
    /// </summary>
    public static async Task<bool> AwaitAsync(SslStream ssl, Task handshake, int timeoutMs)
    {
        Task timeout = Task.Delay(timeoutMs, CancellationToken.None);
        if (await Task.WhenAny(handshake, timeout).ConfigureAwait(false) == timeout)
        {
            try
            {
                ssl.Dispose(); // 폐기가 진행 중 핸드셰이크를 즉시 실패시킨다.
            }
            catch
            {
                // 폐기 실패가 상한 경로를 막으면 안 된다.
            }

            // 중단된 핸드셰이크 태스크의 예외를 관찰 — 미관찰 예외 방지.
            _ = handshake.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
            return false;
        }

        await handshake.ConfigureAwait(false); // 실패 예외 전파
        return true;
    }
}
