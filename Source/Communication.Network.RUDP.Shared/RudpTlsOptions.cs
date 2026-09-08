using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Communication.Network.RUDP;

/// <summary>
/// 클라이언트가 서버 인증서를 검증하는 콜백. 인수는 서버 인증서 DER 바이트이며,
/// <c>true</c>를 반환해야만 핸드셰이크가 계속된다. 핀닝(지문 비교) 구현에 사용한다.
/// </summary>
/// <remarks>무조건 <c>true</c>를 반환하는 콜백은 중간자 공격을 여는 것이므로 금지한다 —
/// 지문·신뢰 체인을 정확히 검사한다.</remarks>
public delegate bool RudpRemoteCertificateValidation(byte[] serverCertificateDer);

/// <summary>
/// RUDP 전송 TLS(DTLS 1.2, BouncyCastle) 옵션. <see cref="RudpTransportOptions.Tls"/>에 설정하면
/// 연결 확립(LiteNetLib 키 수락) 후 신뢰 채널 위에서 DTLS 핸드셰이크를 완료한 뒤 채널을 전달한다.
/// 기본(<c>null</c>)은 평문 — 기존 동작 그대로.
/// </summary>
/// <remarks>
/// TCP의 <c>TcpTlsOptions</c>와 같은 계열이지만 검증 위임이 다르다: OS 인증서 스토어 검증이 없으므로
/// 클라이언트는 <see cref="RemoteCertificateValidation"/>(핀닝) 또는 <see cref="TargetHost"/>(이름 일치) 중
/// 하나를 반드시 설정해야 한다 — 미설정 시 서버 인증서를 **기본 거부**한다(fail-closed).
/// BouncyCastle 타입은 공개면에 노출되지 않는다(ADR 0007 은닉 패턴과 동일).
/// </remarks>
public sealed class RudpTlsOptions
{
    private string? _targetHost;

    /// <summary>기본 핸드셰이크 상한(ms).</summary>
    public const int DefaultHandshakeTimeoutMs = 15_000;

    /// <summary>
    /// 서버 인증서(<see cref="X509Certificate2"/> — 개인 키 포함 필수). **리스너에서만 사용**.
    /// 서명 키는 RSA-2048+ 또는 ECDSA P-256을 권장한다(스위트는 ECDHE + AES-GCM).
    /// 핸드셰이크 실패·상한 초과 연결은 즉시 정리되고 수락은 계속된다(슬롯 회수 포함).
    /// </summary>
    /// <remarks>개인 키를 포함해야 하므로 TCP의 <c>X509Certificate</c>와 달리 <see cref="X509Certificate2"/> 타입이다.</remarks>
    public X509Certificate2? ServerCertificate { get; set; }

    /// <summary>
    /// 클라이언트가 검증할 대상 호스트명 — 인증서 SAN(dNSName)·CN과 대소문자 무시 일치 검사.
    /// <see cref="RemoteCertificateValidation"/>이 설정돼 있으면 이 값은 무시된다.
    /// IP로 접속하는데 인증서가 호스트명으로 발급된 경우 등에 사용한다. **클라이언트에서만 쓰인다.**
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
    /// 서버 인증서 검증 콜백(**클라이언트 전용**) — 게임 전용 서버는 공개 CA·도메인이 없는 경우가 많아
    /// <see cref="RudpTlsOptions.GetSha256Fingerprint"/>로 비교하는 핀닝이 표준 경로다.
    /// 미설정 시 <see cref="TargetHost"/> 이름 일치로, 둘 다 없으면 기본 거부한다.
    /// </summary>
    public RudpRemoteCertificateValidation? RemoteCertificateValidation { get; set; }

    /// <summary>
    /// DTLS 핸드셰이크 상한(ms). 연결만 열고 핸드셰이크를 끌어안는 슬로로리스 방어 —
    /// 상한 내에 완료되지 않으면 연결을 끊는다(서버: 채널 폐기·슬롯 회수, 클라이언트: 연결 실패 확정).
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

    /// <summary>핀닝 비교·표시용 SHA-256 지문(16진수, 콜론 구분). 인수는 인증서 DER 바이트.</summary>
    public static string GetSha256Fingerprint(byte[] certificateDer)
    {
        if (certificateDer is null) throw new ArgumentNullException(nameof(certificateDer));
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(certificateDer);
        return string.Join(':', Array.ConvertAll(hash, static b => b.ToString("x2")));
    }

    private int _handshakeTimeoutMs = DefaultHandshakeTimeoutMs;
}
