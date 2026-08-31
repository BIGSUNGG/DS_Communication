namespace Communication.Shared.Messages;

/// <summary>
/// 송신 큐·수신 디스패치 옵션.
/// </summary>
public sealed class MessageQueueOptions
{
    private int _maxPendingMessages = 10_000;
    private int _coalesceLimitBytes = 64 * 1024;

    /// <summary>
    /// 송신 큐 백프레셔 상한. 상한 도달 시 송신은 공간이 날 때까지 비동기 대기한다.
    /// 수신 디스패치 큐에도 동일 상한이 적용된다.
    /// </summary>
    /// <remarks>
    /// 한계: 메시지 단위 채널(<c>IMessageChannel</c>) 경로에서 수진은 채널 콜백으로 들어오는데,
    /// 콜백 스레드를 막지 않으려 슬롯 대기를 비동기로 넘기므로 핸들러가 밀리면 대기자(메시지 보유)가 이 상한을 넘어 무제한 누적될 수 있다.
    /// 바이트 채널 경로는 단일 수신 루프가 슬롯 대기로 추가 읽기를 막으므로 상한이 유지된다.
    /// </remarks>
    public int MaxPendingMessages
    {
        get => _maxPendingMessages;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            _maxPendingMessages = value;
        }
    }

    /// <summary>
    /// <c>true</c>면 수신 경로에서 핸들러를 즉시 호출한다(큐 없음). 느린 핸들러가 수신을 막으므로 핫패스 전용.
    /// 기본 <c>false</c> — 내부 큐 + 별도 디스패치 루프.
    /// </summary>
    public bool InlineDispatch { get; set; }

    /// <summary>
    /// 바이트 채널 송신 시 한 번의 write로 묶는 배치 상한(바이트). 상한에 닿으면 즉시 전송한다.
    /// 상한 확인은 프레임 추가 후라 배치가 최대 1프레임만큼 상한을 초과할 수 있다.
    /// </summary>
    public int CoalesceLimitBytes
    {
        get => _coalesceLimitBytes;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            _coalesceLimitBytes = value;
        }
    }
}
