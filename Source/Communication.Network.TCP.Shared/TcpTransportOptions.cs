using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Communication.Network.TCP;

/// <summary>
/// TCP 전송 옵션. 미설정은 전부 기본(건드리지 않음)이다 — <see cref="NoDelay"/>만 제외하고(기본 <c>true</c>).
/// </summary>
public sealed class TcpTransportOptions
{
    /// <summary>OS keep-alive 설정. <c>null</c>이면 keep-alive를 건드리지 않는다(OS 기본).</summary>
    public SocketKeepAliveOptions? KeepAlive { get; set; }

    /// <summary>
    /// Nagle 해제(TCP_NODELAY) 여부. 기본 <c>true</c> — 라이브러리가 이미 coalesce로 송신을 묶으므로
    /// Nagle은 중복 지연만 추가한다. <c>false</c>면 OS 설정(Nagle 켜짐)을 유지한다.
    /// </summary>
    public bool NoDelay { get; set; } = true;

    /// <summary>
    /// 동시 수락 연결 수 상한. 상한 도달 시 새 수락 연결은 즉시 닫히고 수락은 계속된다(연결 고갈 공격 방어).
    /// <c>null</c>이면 무제한. 채널(세션)이 Dispose되면 수에서 빠진다.
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
    /// 연결 시도 상한(ms). 호스트가 응답하지 않으면(반개방 경로) OS SYN 재시도가 수십 초까지 끈다 —
    /// 이 값을 설정하면 그 이내에 연결 실패(<c>false</c>)를 확정한다. <c>null</c>이면 OS 기본.
    /// 취소(<see cref="CancellationToken"/>)와는 독립 — 취소는 여전히 <see cref="OperationCanceledException"/>.
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
    private int? _connectTimeout;
}

/// <summary>
/// TCP keep-alive 사용자 설정. half-open 감지 보조용이며 앱 하트비트와 별개다.
/// 플랫폼(특히 Unity Player)에 따라 <see cref="IdleTime"/>/<see cref="Interval"/>은 무시될 수 있다.
/// </summary>
public sealed class SocketKeepAliveOptions
{
    /// <summary>keep-alive 적용 여부. <c>false</c>면 옵션 전체가 무시된다.</summary>
    public bool Enabled { get; set; }

    /// <summary>연결 유휴 후 첫 probe까지 시간. <see cref="TimeSpan.Zero"/>면 기본 유지(가능한 플랫폼만).</summary>
    public TimeSpan IdleTime { get; set; } = TimeSpan.Zero;

    /// <summary>probe 간격. <see cref="TimeSpan.Zero"/>면 기본 유지(가능한 플랫폼만).</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.Zero;
}

/// <summary>
/// keep-alive 소켓 옵션 적용. 플랫폼 미지원 필드는 조용히 무시한다(문서화된 플랫폼 노트).
/// </summary>
internal static class KeepAliveApplicator
{
    // SIO_KEEPALIVE_VALS (Windows): [onoff 4B][keepalivetime ms 4B][keepaliveinterval ms 4B]
    private const int SioKeepAliveVals = unchecked((int)0x98000004);

    public static void Apply(Socket socket, SocketKeepAliveOptions? options)
    {
        if (options is null || !options.Enabled)
        {
            return;
        }

        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }
        catch
        {
            return; // keep-alive 자체를 켤 수 없으면 세부 값도 무의미.
        }

        if (options.IdleTime <= TimeSpan.Zero && options.Interval <= TimeSpan.Zero)
        {
            return; // 켜기만 요청됨.
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ApplyWindows(socket, options);
        }
        else
        {
            ApplyUnix(socket, options);
        }
    }

    private static void ApplyWindows(Socket socket, SocketKeepAliveOptions options)
    {
        // SIO_KEEPALIVE_VALS는 세 값을 한 번에 재설정한다. 미지정 필드는 OS 기본값으로 채운다.
        int idleMs = options.IdleTime > TimeSpan.Zero ? (int)options.IdleTime.TotalMilliseconds : 7_200_000;
        int intervalMs = options.Interval > TimeSpan.Zero ? (int)options.Interval.TotalMilliseconds : 1_000;

        byte[] input = new byte[12];
        BitConverter.GetBytes(1).CopyTo(input, 0);
        BitConverter.GetBytes(idleMs).CopyTo(input, 4);
        BitConverter.GetBytes(intervalMs).CopyTo(input, 8);

        try
        {
            socket.IOControl(SioKeepAliveVals, input, null);
        }
        catch
        {
            // 무시 — 플랫폼 노트 대상.
        }
    }

    private static void ApplyUnix(Socket socket, SocketKeepAliveOptions options)
    {
        // Linux: TCP_KEEPIDLE=4, TCP_KEEPINTVL=5 / macOS 유휴: TCP_KEEPALIVE=16.
        // 미지원 번호는 예외로 조용히 걸러진다.
        if (options.IdleTime > TimeSpan.Zero)
        {
            int idleSeconds = (int)options.IdleTime.TotalSeconds;
            TrySetRaw(socket, 4, idleSeconds);
            TrySetRaw(socket, 16, idleSeconds);
        }

        if (options.Interval > TimeSpan.Zero)
        {
            TrySetRaw(socket, 5, (int)options.Interval.TotalSeconds);
        }
    }

    private static void TrySetRaw(Socket socket, int optionNumber, int value)
    {
        if (value <= 0)
        {
            return;
        }

        try
        {
            socket.SetSocketOption(SocketOptionLevel.Tcp, (SocketOptionName)optionNumber, value);
        }
        catch
        {
            // 플랫폼 미지원 — 무시.
        }
    }
}
