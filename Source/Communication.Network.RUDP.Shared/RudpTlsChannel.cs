using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;
using Org.BouncyCastle.Tls;
using SharedDisconnectReason = Communication.Shared.Connection.DisconnectReason;

namespace Communication.Network.RUDP;

/// <summary>
/// DTLS 세션이 성립한 뒤 평문 채널을 감싸는 랩 — 송신은 <c>DtlsTransport.Send</c>로 레코드를 만들고,
/// 수신은 비동기 펌프가 레코드를 복호화해 <see cref="MessageReceived"/>를 발화한다.
/// 앱은 <c>IMessageChannel</c>로만 보므로 BouncyCastle 타입이 공개면에 노출되지 않는다(ADR 0007 은닉 패턴).
/// </summary>
/// <remarks>
/// <b>스레딩</b>: BC의 <c>DtlsTransport</c>는 스레드 안전하지 않다 — 송신·펌프 수신·Close를 단일 락으로 직렬화한다.
/// 펌프는 대기 중 비용이 없는 비동기 태스크다(스레드 1개/접속이 아니다). 복호화는 폴링 스레드가 아니라
/// 이 펌프에서 일어나 — 단일 폴링 스레드가 모든 접속의 암호 연산을 점유하지 않는다.
/// </remarks>
internal sealed class RudpTlsChannel : IMessageChannel, IRudpTransportEvents
{
    private const int MaxRecordSize = 65_507; // RudpDtlsTransport의 데이터그램 상한과 일치
    private const int MaxRecordPlaintext = 16_384; // TLS 명세상 레코드 평문 상한(압축 길이 uint16)
    private const int EnvelopeSize = 3; // [flags(1)][len(2)] — 청킹·경계 보존용 내부 봉투
    private const int ChunkDataSize = MaxRecordPlaintext - EnvelopeSize;
    private const int MaxMessageLength = 64 * 1024 * 1024; // 수신 재조립 상한 — TCP 절대 상한과 같은 기준
    private const byte FlagStart = 0x01; // 이 레코드가 메시지의 시작이다
    private const byte FlagEnd = 0x02; // 이 레코드가 메시지의 끝이다(단일 레코드면 Start|End)

    private readonly RudpMessageChannel _inner;
    private readonly RudpDtlsTransport _transport;
    private readonly DtlsTransport _dtls;
    private readonly object _gate = new();
    private readonly Task _pump;
    private int _disposed;

    internal RudpTlsChannel(RudpMessageChannel inner, RudpDtlsTransport transport, DtlsTransport dtls)
    {
        _inner = inner;
        _transport = transport;
        _dtls = dtls;
        _inner.TransportDisconnected += OnInnerDisconnected;
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>전송 계층 관점의 연결 상태 — 랩이 정리되지 않았고 내부 채널이 살아 있어야 <c>true</c>.</summary>
    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _inner.IsConnected;

    /// <summary>
    /// 수신 메시지 알림(복호화된 평문). 펌프 태스크에서 발생하며 payload는 콜백 안에서만 유효하다.
    /// </summary>
    public event Action<ReadOnlyMemory<byte>>? MessageReceived;

    /// <summary>내부 채널의 peer 끊김을 그대로 이어 붙인다 — <see cref="RudpSession"/>이 구독한다.</summary>
    internal event Action<SharedDisconnectReason>? TransportDisconnected;

    /// <summary>
    /// payload 하나를 지정한 전송 방식으로 보낸다 — DTLS 레코드로 감싸져 내부 채널로 나간다.
    /// 레코드 오버헤드(약 13~29바이트)가 추가된다. 16,381바이트 초과 메시지는 <see cref="RudpDeliveryMethod.ReliableOrdered"/>
    /// 로만 전송한다(다중 레코드 청킹 + 수신 재조립) — 그 외 방식은 기존 MTU 가드 철학대로 사전 거부한다.
    /// </summary>
    /// <exception cref="ArgumentException">빈 payload 또는 비분할 방식으로 청킹 상한을 넘은 경우.</exception>
    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, SendOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Faulted(new OperationCanceledException(cancellationToken));
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return Faulted(new InvalidOperationException("채널이 정리되어 송신할 수 없습니다."));
        }

        if (payload.Length == 0)
        {
            return Faulted(new ArgumentException("빈 payload는 보낼 수 없습니다.", nameof(payload)));
        }

        RudpDeliveryMethod method = (options as RudpSendOptions)?.DeliveryMethod ?? RudpDeliveryMethod.ReliableOrdered;

        // 청킹은 순서가 보장된 스트림 위에서만 성립한다 — 재조립이 어긋나지 않는 유일한 방식이다.
        // 비신뢰·시퀀스 방식은 단일 레코드 상한을 지켜야 한다(기존 MTU 사전 거부와 같은 계약).
        if (payload.Length > ChunkDataSize && method != RudpDeliveryMethod.ReliableOrdered)
        {
            return Faulted(new ArgumentException(
                $"전송 방식 {method}는 청킹할 수 없어 payload가 {ChunkDataSize}바이트 이하여야 합니다 (요청 {payload.Length}바이트). " +
                $"더 큰 메시지는 {nameof(RudpDeliveryMethod.ReliableOrdered)}로 보내십시오."));
        }

        try
        {
            lock (_gate)
            {
                // dtls.Send → transport.Send 호출 사슬이 동기다 — 락 안에서 설정·해제하면
                // 레코드 하나에 정확히 이 메시지의 전송 방식이 담긴다(신뢰성이 몰래 바뀌지 않는다).
                _transport.SetNextDelivery(method);
                try
                {
                    for (int offset = 0; offset < payload.Length; offset += ChunkDataSize)
                    {
                        int take = Math.Min(ChunkDataSize, payload.Length - offset);

                        // 시작 비트는 첫 조각에만, 끝 비트는 기본 켜두고 마지막이 아니면 끈다 —
                        // offset>0에서 0으로 초기화하면 마지막 조각의 End까지 지워져 재조립이 영원히 끝나지 않는다.
                        byte flags = (byte)((offset == 0 ? FlagStart : 0) | FlagEnd);
                        if (offset + take < payload.Length)
                        {
                            flags &= unchecked((byte)~FlagEnd); // 아직 끝나지 않았다
                        }

                        byte[] record = new byte[EnvelopeSize + take];
                        record[0] = flags;
                        record[1] = (byte)(take >> 8);
                        record[2] = (byte)take;
                        payload.Span.Slice(offset, take).CopyTo(record.AsSpan(EnvelopeSize));
                        _dtls.Send(record, 0, record.Length);
                    }
                }
                finally
                {
                    _transport.SetNextDelivery(null);
                }
            }

            return default;
        }
        catch (Exception e)
        {
            return Faulted(e);
        }
    }

    /// <summary>
    /// close_notify를 보내 정상 종료를 알린 뒤(신뢰 채널 — 상대에게 전달된다) 내부 채널을 정리한다.
    /// 중복 호출은 무시된다.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                _dtls.Close();
            }
        }
        catch
        {
            // 이미 끊긴 피어로의 close_notify 송신 실패가 정리를 막으면 안 된다.
        }

        _transport.Close(); // 펌프 깨우기 — 다음 Receive가 실패하며 펌프가 빠져나온다.
        _inner.Dispose(); // 슬롯 회수 + (클라이언트) 호스트까지 정리
    }

    /// <summary>구독 직후 래치 회수 대행 — 세션 생성 창구의 단절을 소실하지 않는다.</summary>
    bool IRudpTransportEvents.TryConsumeLatchedDisconnect(out SharedDisconnectReason reason)
        => ((IRudpTransportEvents)_inner).TryConsumeLatchedDisconnect(out reason);

    /// <summary>세션 구독 추가 대행 — 내부 이벤트에 그대로 붙인다.</summary>
    void IRudpTransportEvents.AddTransportDisconnectedHandler(Action<SharedDisconnectReason> handler)
        => TransportDisconnected += handler;

    private void OnInnerDisconnected(SharedDisconnectReason reason)
    {
        TransportDisconnected?.Invoke(reason);
        _transport.Close(); // 죽은 피어로 close_notify를 보내지 않는다 — 전송만 즉시 폐쇄(펌프도 여기서 빠져나온다).
    }

    private async Task PumpAsync()
    {
        byte[] buffer = new byte[MaxRecordSize];
        while (Volatile.Read(ref _disposed) == 0)
        {
            await _transport.WaitDataAsync().ConfigureAwait(false); // 도착·폐쇄 신호 — 대기 중 비용 없음

            try
            {
                // 큐에 원시 레코드가 남아 있을 때만 Receive한다 — 빈 큐 호출은 BC 수신의 무기한
                // 블록(펌프 락 점유 → 송신 교찰)을 부른다. 세마포어 신호와 큐 잔량은 시점이 어긋날 수 있다.
                while (_transport.HasPendingData)
                {
                    int n;
                    lock (_gate)
                    {
                        n = _dtls.Receive(buffer, 0, buffer.Length, 0);
                    }

                    if (n <= 0)
                    {
                        break; // BC가 아직 소비하지 않았다 — 다음 도착 신호에서 재시도한다.
                    }

                    try
                    {
                        ProcessRecord(buffer, n); // 봉투([flags][len][data])를 풀어 메시지 경계를 복원한다
                    }
                    catch (Exception e)
                    {
                        // 구독자 예외가 펌프를 죽이면 이후 수신이 모두 유실된다 — 격리 후 계속(폴링 경로와 동일 원칙).
                        Trace.TraceError($"RUDP TLS 수신 핸들러 예외 — 격리 후 계속: {e}");
                    }
                }
            }
            catch (Exception)
            {
                return; // 전송 폐쇄·복호화 실패 — 펌프 종료(끊김은 TransportDisconnected 경로로 이미 전달됐다)
            }
        }
    }

    // netstandard2.1에는 ValueTask.FromException이 없다(.NET 5+) — Task 경유로 예외 완료 ValueTask를 만든다.
    private static ValueTask Faulted(Exception error) => new(Task.FromException(error));

    /// <summary>재조립 상태 — 송신측 청킹 봉투의 수신측 대응. 펌프 스레드에서만 접근한다.</summary>
    private byte[]? _pendingMessage; // null이면 조립 중이 아니다
    private int _pendingLength;

    /// <summary>
    /// 복호화된 레코드 한 개의 봉투([flags(1)][len(2)][data])를 푼다 — 단일 레코드는 즉시 발화,
    /// 다중 청크는 재조립 후 발화한다. 봉투는 송신측이 항상 붙이므로 정상 호환끼리는 어긋나지 않는다.
    /// </summary>
    private void ProcessRecord(byte[] record, int length)
    {
        if (length < EnvelopeSize)
        {
            FailProtocol($"봉투보다 짧은 레코드({length}바이트)");
            return;
        }

        byte flags = record[0];
        int declared = (record[1] << 8) | record[2];
        if (declared != length - EnvelopeSize)
        {
            FailProtocol($"봉투 길이 불일치(선언 {declared}, 실제 {length - EnvelopeSize})");
            return;
        }

        bool start = (flags & FlagStart) != 0;
        bool end = (flags & FlagEnd) != 0;

        if (start)
        {
            _pendingMessage = null; // 진행 중 조립이 있으면 폐기 — 순서 스트림 위에서는 불가능한 상황(방어)
            _pendingLength = 0;

            if (declared == 0)
            {
                FailProtocol("빈 메시지"); // 송신측이 만들지 않는 형태 — 위반 취급
                return;
            }

            if (end)
            {
                Deliver(record.AsMemory(EnvelopeSize, declared));
                return;
            }

            _pendingMessage = new byte[declared];
            Buffer.BlockCopy(record, EnvelopeSize, _pendingMessage, 0, declared);
            _pendingLength = declared;
            return;
        }

        // 이어지는 조각
        if (_pendingMessage is null || _pendingLength == 0)
        {
            return; // 시작 없는 조각 — 폐기(신뢰 스트림에서는 불가능, 위변조 방어)
        }

        if ((long)_pendingLength + declared > MaxMessageLength)
        {
            FailProtocol($"재조립 상한 초과({_pendingLength + declared}바이트)");
            return;
        }

        if (_pendingLength + declared > _pendingMessage.Length)
        {
            int grown = Math.Max(_pendingMessage.Length * 2, _pendingLength + declared);
            byte[] resized = new byte[grown];
            Buffer.BlockCopy(_pendingMessage, 0, resized, 0, _pendingLength);
            _pendingMessage = resized;
        }

        Buffer.BlockCopy(record, EnvelopeSize, _pendingMessage, _pendingLength, declared);
        _pendingLength += declared;

        if (end)
        {
            byte[] message = _pendingMessage!;
            int messageLength = _pendingLength;
            _pendingMessage = null;
            _pendingLength = 0;
            Deliver(message.AsMemory(0, messageLength));
        }
    }

    private void Deliver(ReadOnlyMemory<byte> message)
    {
        MessageReceived?.Invoke(message); // payload는 콜백 안에서만 유효하다(IMessageChannel 계약)
    }

    /// <summary>봉투 위반 — fail-closed. 채널을 폐쇄해 세션이 단절을 관측하게 한다(TCP 프레이머와 동일 원칙).</summary>
    private void FailProtocol(string cause)
    {
        Trace.TraceError($"RUDP TLS 메시지 봉투 위반 — 채널 폐기(fail-closed): {cause}");
        Dispose();
    }
}
