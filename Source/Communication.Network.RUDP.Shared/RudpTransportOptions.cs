using System;

namespace Communication.Network.RUDP;

/// <summary>
/// RUDP 전송 옵션. 미설정 시 전부 기본값이며, LiteNetLib의 나머지 튜닝 값은 건드리지 않는다.
/// </summary>
public sealed class RudpTransportOptions
{
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

    private int? _maxConnections;
    private int _disconnectTimeout = DefaultDisconnectTimeoutMs;
    private string _connectionKey = DefaultConnectionKey;
}
