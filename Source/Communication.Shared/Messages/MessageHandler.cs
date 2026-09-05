using System;

using Communication.Shared.Sessions;

namespace Communication.Shared.Messages;

/// <summary>
/// 타입 등록 기반 수신 디스패처. <c>RegisterMessageType</c>/<see cref="Register{T}"/>로
/// 타입별 <see cref="Action{T}"/>을 등록하고 <see cref="HandleMessage"/>가 분배한다.
/// 등록 테이블은 동시 안전 — 디스패치 중 지연 등록도 경쟁이 없다.
/// 정확 타입이 없으면 등록된 베이스 타입(상속·인터페이스) 중 가장 구체적인 것으로 분배하고,
/// 맞는 핸들러가 전혀 없을 때만 Trace 후 skip한다. 핸들러 예외는 Trace 후 계속 — 수신 루프를 죽이지 않는다.
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

        Type messageType = message.GetType();
        if (_handlers.TryGetValue(messageType, out Action<object>? exact))
        {
            InvokeHandler(exact, message);
            return;
        }

        // 정확 타입 미등록 — 등록된 베이스 타입(상속·인터페이스) 중 가장 구체적인 것으로 분배한다.
        // (미등록 skip은 조용한 메시지 유실이므로 맞는 핸들러가 하나라도 있으면 버리지 않는다.
        //  컨버터가 다형 직렬화를 쓸 때 파생 타입이 도착하는데, 베이스만 등록했다면 이 분배가 유일한 수신 경로다.
        //  등록 수는 작다고 가정 — 매 miss마다 선형 스캔. 동률(인터페이스·클래스 교차)이면 먼저 발견된 쪽.)
        Type? bestKey = null;
        foreach (KeyValuePair<Type, Action<object>> pair in _handlers)
        {
            if (!pair.Key.IsAssignableFrom(messageType))
            {
                continue;
            }

            // 더 구체적인(파생된) 등록 타입이 이긴다 — best가 pair.Key의 베이스면 교체.
            if (bestKey is { } best && best.IsAssignableFrom(pair.Key))
            {
                bestKey = pair.Key;
            }
            else if (bestKey is null)
            {
                bestKey = pair.Key;
            }
        }

        if (bestKey is not null)
        {
            InvokeHandler(_handlers[bestKey], message);
            return;
        }

        System.Diagnostics.Trace.TraceWarning($"미등록 메시지 타입 {messageType.Name} — skip");
    }

    private static void InvokeHandler(Action<object> handler, object message)
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
}
