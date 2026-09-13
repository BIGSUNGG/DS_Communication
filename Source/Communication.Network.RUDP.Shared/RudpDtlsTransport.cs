using System;
using System.Buffers;
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
internal class RudpDtlsTransport : DatagramTransport, IDisposable
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
    {
        if (_closed)
        {
            throw new IOException("RUDP DTLS 전송이 닫혔습니다.");
        }

        // BC(netstandard 빌드)가 부르는 경로 — BC 내부 재사용 버퍼를 복사 없이 Memory로 넘긴다.
        // 채널(LiteNetLib) 송신은 동기 복사로 완료되므로 GetResult() 반환 시점에 데이터는 이미
        // LiteNetLib 패킷으로 옮겨졌다(버퍼 수명 유효). 이전 구현의 record.ToArray() 이중 복사 제거.
        _channel.SendAsync(buf.AsMemory(off, len), new RudpSendOptions(_nextDelivery ?? RudpDeliveryMethod.ReliableOrdered))
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Span 오버로드 — net6.0 BC 빌드가 부르는 경로. Span에는 소유자가 없어 풀에서 빌려 복사한다(할당 0).
    /// <see cref="Receive(Span{byte},int)"/>와 같은 이유로 virtual로 발행한다(BC 이중 빌드 호환).
    /// </summary>
    public virtual void Send(ReadOnlySpan<byte> record)
    {
        if (_closed)
        {
            throw new IOException("RUDP DTLS 전송이 닫혔습니다.");
        }

        byte[] packet = ArrayPool<byte>.Shared.Rent(record.Length);
        try
        {
            record.CopyTo(packet);
            _channel.SendAsync(packet.AsMemory(0, record.Length), new RudpSendOptions(_nextDelivery ?? RudpDeliveryMethod.ReliableOrdered))
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packet);
        }
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

    /// <summary>
    /// 전송을 닫고 대기 신호세마포어까지 폐기한다. 닫힌 뒤 남을 수 있는 유일한 자원(세마포어)의
    /// 결정적 정리 — 폐기 후 새 대기(<see cref="WaitDataAsync"/>·동기 <c>Wait</c>)는 예외로 풀리는데,
    /// 남은 대기자는 이미 폐쇄 경로(펌프 탈출·중단된 핸드셰이크)를 타고 있으므로 의도된 종료 신호로 쓰인다.
    /// 이중 폐기·Close 후 재호출은 모두 무해하다.
    /// </summary>
    public void Dispose()
    {
        Close();
        _available.Dispose();
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
        if (_closed)
        {
            return; // 폐쇄 후 늦게 도착한 레코드 — 폐기된 세마포어에 신호를 쏘지 않고 버린다.
        }

        // payload는 콜백 안에서만 유효하다(IMessageChannel 계약) — 복사해 소유한다.
        _inbound.Enqueue(record.ToArray());
        try
        {
            _available.Release();
        }
        catch (ObjectDisposedException)
        {
            // 폐기 경쟁 창(닫힘 검사와 Release 사이에 Dispose) — 늦은 레코드와 함께 조용히 버린다.
        }
    }

    private void OnTransportDisconnected(SharedDisconnectReason _) => Close();
}
