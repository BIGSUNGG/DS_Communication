using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Tls;
using SharedDisconnectReason = Communication.Shared.Connection.DisconnectReason;

namespace Communication.Network.RUDP;

/// <summary>
/// BouncyCastle DTLS <c>DatagramTransport</c>를 RUDP 채널 위에 올리는 어댑터.
/// 채널의 push 기반 <c>MessageReceived</c>를 큐로 받아 BC의 pull 기반 <c>Receive</c>에 제공하고,
/// BC가 기록한 레코드를 채널 송신(기본 ReliableOrdered)으로 흘린다.
/// </summary>
/// <remarks>
/// 핸드셰이크(동기 스레드)와 데이터 단계(랩 채널의 펌프)가 같은 큐를 공유한다 —
/// 수신 소비자는 시점상 항상 하나뿐이다. 레코드 하나가 와이어에서는 메시지 하나가 된다
/// (큰 레코드는 LiteNetLib 신뢰 채널이 분할·재조립).
/// </remarks>
internal class RudpDtlsTransport : DatagramTransport
{
    private const int DatagramLimit = 65_507; // UDP 데이터그램 최대 — 레코드 크기 상한

    private readonly RudpMessageChannel _channel;
    private readonly ConcurrentQueue<byte[]> _inbound = new();
    private readonly SemaphoreSlim _available = new(0);
    private volatile bool _closed;

    /// <summary>
    /// 다음 송신 레코드에 적용할 전송 방식. 랩 채널이 <c>DtlsTransport.Send</c> 직전에 설정하고
    /// 같은 락 안에서 해제한다 — 송신 호출 사슬이 동기라는 성질에 의존한다(핸드셰이크 송신은 항상 ReliableOrdered).
    /// </summary>
    private RudpDeliveryMethod? _nextDelivery;

    internal RudpDtlsTransport(RudpMessageChannel channel)
    {
        _channel = channel;
        _channel.MessageReceived += OnRecordReceived;
        _channel.TransportDisconnected += OnTransportDisconnected;
    }

    public int GetReceiveLimit() => DatagramLimit;

    public int GetSendLimit() => DatagramLimit;

    /// <summary>수신 레코드를 꺼낸다. 상한 대기 후 데이터가 없으면 0(BC 재전송 로직이 처리), 닫혀 있으면 예외.</summary>
    public int Receive(byte[] buf, int off, int len, int waitMillis)
        => Receive(buf.AsSpan(off, len), waitMillis);

    /// <summary>
    /// Span 오버로드 — 실제 구현. 수신 소비자(핸드셰이크 스레드·데이터 펌프)가 이 경로로 호출된다.
    /// <b>virtual 필수</b>: BC netstandard2.0 빌드에는 Span 오버로드가 없어 컴파일타임엔 인터페이스 구현으로
    /// 묶이지 않는다 — .NET 6+ 호스트가 net6.0 빌드(추가 Span 추상 멤버 포함)를 로드하면
    /// 비가상 메서드는 인터페이스 슬롯에 못 묶여 TypeLoadException이 난다. virtual로 발행해 양쪽 빌드를 모두 만족시킨다.
    /// </summary>
    public virtual int Receive(Span<byte> buffer, int waitMillis)
    {
        while (true)
        {
            if (_inbound.TryDequeue(out byte[]? record))
            {
                int n = Math.Min(buffer.Length, record.Length);
                record.AsSpan(0, n).CopyTo(buffer);
                return n;
            }

            if (_closed)
            {
                throw new IOException("RUDP DTLS 전송이 닫혔습니다.");
            }

            if (waitMillis == 0 || !_available.Wait(waitMillis))
            {
                return 0;
            }
        }
    }

    /// <summary>BC가 인코딩한 레코드 하나를 채널로 보낸다. 닫혀 있으면 예외.</summary>
    public void Send(byte[] buf, int off, int len)
        => Send(buf.AsSpan(off, len));

    /// <summary>
    /// Span 오버로드 — 실제 구현. 레코드를 복사해 채널 송신(기본 ReliableOrdered)으로 넘긴다.
    /// <see cref="Receive(Span{byte},int)"/>와 같은 이유로 virtual로 발행한다(BC 이중 빌드 호환).
    /// </summary>
    public virtual void Send(ReadOnlySpan<byte> record)
    {
        if (_closed)
        {
            throw new IOException("RUDP DTLS 전송이 닫혔습니다.");
        }

        byte[] packet = record.ToArray();

        // LiteNetLib 송신은 동기 완료 — ValueTask를 그대로 푼다(실패 예외 전파).
        _channel.SendAsync(packet, new RudpSendOptions(_nextDelivery ?? RudpDeliveryMethod.ReliableOrdered))
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>전송을 닫는다. 진행 중 <see cref="Receive"/> 대기는 즉시 실패로 풀린다(핸드셰이크 상한 중단 경로).</summary>
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _available.Release(); // 대기자가 _closed를 보게 한다.
    }

    /// <summary>펌프가 새 레코드 도착을 비동기로 기다린다 — 도착·폐쇄 둘 다 깨운다.</summary>
    internal Task WaitDataAsync() => _available.WaitAsync();

    /// <summary>
    /// 큐에 소비 안 된 원시 레코드가 있는지 — 펌프가 <c>DtlsTransport.Receive</c>를
    /// 빈 큐에서 호출하지 않게 하는 가드. BC의 Span 기반 수신은 대기 없이(0ms)도 완전한 레코드가
    /// 도착할 때까지 블록할 수 있어, 빈 큐 호출은 펌프 락 영구 점유(=송신 교착)로 이어진다.
    /// </summary>
    internal bool HasPendingData => !_inbound.IsEmpty;

    /// <summary>랩 채널 송신이 락 안에서 호출 — 레코드 하나에 적용할 전송 방식을 지정한다.</summary>
    internal void SetNextDelivery(RudpDeliveryMethod? method) => _nextDelivery = method;

    private void OnRecordReceived(ReadOnlyMemory<byte> record)
    {
        // payload는 콜백 안에서만 유효하다(IMessageChannel 계약) — 복사해 소유한다.
        _inbound.Enqueue(record.ToArray());
        _available.Release();
    }

    private void OnTransportDisconnected(SharedDisconnectReason _) => Close();
}
