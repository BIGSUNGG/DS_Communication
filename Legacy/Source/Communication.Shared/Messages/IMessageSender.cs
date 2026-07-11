namespace Communication.Shared.Messages;

public interface IMessageSender
{
    Task SendAsync(object message, object context);
    Task SendAsync(object message);

    /// <summary>
    /// 메시지를 큐에 넣은 뒤, 해당 메시지가 wire에 기록될 때까지 대기합니다.
    /// </summary>
    Task SendAndFlushAsync(object message, object? context = null, CancellationToken cancellationToken = default);
}
