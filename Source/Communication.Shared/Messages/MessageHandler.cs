using System;

using Communication.Shared.Sessions;

namespace Communication.Shared.Messages;

/// <summary>
/// 타입 등록 기반 수신 디스패처. <c>RegisterMessageType</c>/<see cref="Register{T}"/>로
/// 타입별 <see cref="Action{T}"/>을 등록하고 <see cref="HandleMessage"/>가 분배한다.
/// 등록 테이블은 동시 안전 — 디스패치 중 지연 등록도 경쟁이 없다.
/// 미등록 타입은 Trace 후 skip, 핸들러 예외는 Trace 후 계속 — 수신 루프를 죽이지 않는다.
/// </summary>
public abstract class MessageHandler : IMessageHandler
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Action<object>> _handlers = new();

    /// <summary>핸들러가 속한 세션.</summary>
    protected ISession Session { get; }

    protected MessageHandler(ISession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>타입 → 핸들러 등록. 같은 타입 재등록은 덮어쓴다.</summary>
    protected void RegisterMessageType(Type messageType, Action<object> handler)
    {
        if (messageType is null) throw new ArgumentNullException(nameof(messageType));
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        _handlers[messageType] = handler;
    }

    /// <summary>타입 → 핸들러 등록(제네릭).</summary>
    protected void Register<T>(Action<T> handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        RegisterMessageType(typeof(T), message => handler((T)message));
    }

    public void HandleMessage(object message)
    {
        if (message is null)
        {
            return;
        }

        if (_handlers.TryGetValue(message.GetType(), out var handler))
        {
            try
            {
                handler(message);
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.TraceError($"핸들러 예외({message.GetType().Name}) — 격리 후 계속: {e}");
            }
        }
        else
        {
            System.Diagnostics.Trace.TraceWarning($"미등록 메시지 타입 {message.GetType().Name} — skip");
        }
    }
}
