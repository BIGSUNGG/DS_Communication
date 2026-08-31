namespace Communication.Shared.Messages;

/// <summary>
/// 송신 큐·수신 디스패치 옵션.
/// </summary>
public sealed class MessageQueueOptions
{
    private int _maxPendingMessages = 10_000;
    private int _coalesceLimitBytes = 64 * 1024;

    /// <summary>송신 큐 백프레셔 상한. 상한 도달 시 송신은 공간이 날 때까지 비동기 대기한다.</summary>
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

    /// <summary>바이트 채널 송신 시 한 번의 write로 묶는 배치 상한(바이트). 상한에 닿으면 즉시 전송한다.</summary>
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
