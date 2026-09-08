using System;

namespace Communication.Network.RUDP;

/// <summary>
/// RUDP 전송 옵션. 미설정 시 전부 기본값이며, LiteNetLib의 나머지 튜닝 값은 건드리지 않는다.
/// </summary>
public sealed class RudpTransportOptions
{
    /// <summary>
    /// 전송 TLS(DTLS 1.2) 옵션. 설정 시 연결 확립 후 신뢰 채널 위에서 핸드셰이크를 완료한 뒤 채널을 전달한다.
    /// 서버는 <see cref="RudpTlsOptions.ServerCertificate"/> 설정 시, 클라이언트는 옵션 자체가 설정된 경우 TLS를 켠다.
    /// 기본(<c>null</c>)은 평문 — 기존 동작 그대로. **양단 같은 설정**이어야 한다(와이어 비호환).
    /// BouncyCastle.Cryptography 의존성이 추가되며 BC 타입은 공개면에 노출되지 않는다.
    /// </summary>
    public RudpTlsOptions? Tls { get; set; }

    /// <summary>기본 연결 키. 앱이 토큰·핸드셰이크 식별자로 바꾸지 않는 한 이 값으로 접속을 검증한다.</summary>
    public const string DefaultConnectionKey = "DS_Communication.RUDP";

    /// <summary>기본 끊김 판정 시간(ms). 상대 peer로부터 패킷이 없으면 이 시간 후 끊긴 것으로 본다.</summary>
    public const int DefaultDisconnectTimeoutMs = 5000;

    /// <summary>
    /// 동시 수락 연결 수 상한. 상한 도달 시 접속 요청은 즉시 거부되고 수락은 계속된다(연결 고갈 공격 방어).
    /// <c>null</c>이면 무제한. peer가 끊기거나 채널이 Dispose되면 수에서 빠진다. 서버 쪽에서만 의미가 있다.
    /// </summary>
    public int? MaxConnections
    {
        get => _maxConnections;
        set
        {
            if (value is { } v && v <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            _maxConnections = value;
        }
    }

    /// <summary>
    /// 끊김 판정 시간(ms). 기본 <see cref="DefaultDisconnectTimeoutMs"/>.
    /// UDP는 스트림 끝이 없어 이 값이 half-open 감지의 유일한 신호다 — 앱 하트비트와는 별개다.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">0 이하인 경우.</exception>
    public int DisconnectTimeout
    {
        get => _disconnectTimeout;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            _disconnectTimeout = value;
        }
    }

    /// <summary>
    /// 접속 요청 검증 키. 서버는 이 키와 일치하는 요청만 수락하고, 클라이언트는 이 키로 접속한다.
    /// 기본 <see cref="DefaultConnectionKey"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><c>null</c>인 경우.</exception>
    /// <exception cref="ArgumentException">빈 문자열인 경우.</exception>
    public string ConnectionKey
    {
        get => _connectionKey;
        set
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            if (value.Length == 0) throw new ArgumentException("연결 키는 빈 문자열일 수 없습니다.", nameof(value));
            _connectionKey = value;
        }
    }

    /// <summary>IPv6 소켓도 함께 바인딩할지 여부. 기본 <c>false</c>(IPv4만).</summary>
    public bool IPv6 { get; set; }

    /// <summary>
    /// 패킷 무결성 검사(CRC32c 레이어) 활성화. 송신 패킷마다 CRC32c 체크섬(4바이트)을 붙이고
    /// 수신은 체크섬 위반 패킷을 **프로토콜 처리 전에** 폐기한다 — IPv4 UDP 체크섬은 0(비활성)일 수 있어
    /// 손상 패킷이 앱까지 스며드는 것을 전송 가장자리에서 막는다(위반 접속 요청은 슬롯 예약도 일어나지 않는다).
    /// <b>양단 모두 같은 설정</b>이어야 한다(와이어 비호환 — CRC 켠 쪽은 안 켠 상대와 통신 불가).
    /// 위변조 <b>검출</b>뿐 방지가 아니다 — 키 없는 CRC라 능동 공격자는 재계산할 수 있다. 기밀성·인증은 없음(평문 유지).
    /// 기본 <c>false</c>.
    /// </summary>
    public bool Crc32cEnabled { get; set; }

    /// <summary>
    /// 클라이언트 연결 시도 상한(ms). 호스트가 침묵(패킷 유실·블랙홀)하면 연결 실패는
    /// LiteNetLib의 재전송 소진으로만 결정되는데, 기본값은 약 5초(500ms × 10회)로 고정되어 있다.
    /// 이 값을 설정하면 그 이내에 연결 실패를 확정한다(재전송 간격 100ms 기준 시도 횟수 환산).
    /// <c>null</c>이면 LiteNetLib 기본값을 유지한다. 서버에는 영향을 주지 않는다.
    /// </summary>
    public int? ConnectTimeout
    {
        get => _connectTimeout;
        set
        {
            if (value is { } v && v <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            _connectTimeout = value;
        }
    }

    private int? _maxConnections;
    private int _disconnectTimeout = DefaultDisconnectTimeoutMs;
    private string _connectionKey = DefaultConnectionKey;
    private int? _connectTimeout;
}
