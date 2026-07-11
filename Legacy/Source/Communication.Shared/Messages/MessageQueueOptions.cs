namespace Communication.Shared.Messages;

/// <summary>
/// 송신·핸들러 큐 백프레셔 설정. 기본 MaxPendingMessages는 기존 unbounded에 가깝게 충분히 큽니다.
/// </summary>
public sealed class MessageQueueOptions
{
    public const int DefaultMaxPendingMessages = 10_000;

    public int MaxPendingMessages { get; set; } = DefaultMaxPendingMessages;

    /// <summary>
    /// true이면 Receiver 경로에서 Handler 큐 없이 동기 디스패치합니다. 기본 false.
    /// </summary>
    public bool InlineDispatch { get; set; }
}
