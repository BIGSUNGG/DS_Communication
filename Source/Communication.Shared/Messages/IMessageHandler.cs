namespace Communication.Shared.Messages;

/// <summary>
/// 수신 메시지 핸들러. 동기 계약 — 긴 작업은 앱이 직접 오프로딩한다.
/// 끊김은 다루지 않으며 세션의 <c>Disconnected</c> 이벤트만 사용한다.
/// </summary>
public interface IMessageHandler
{
    void HandleMessage(object message);
}
