using System;
using Communication.Shared.Channels;
using Communication.Shared.Connection;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;

namespace Communication.Network.RUDP;

/// <summary>
/// RUDP 세션. 앱이 채널 위에 직접 생성한다 —
/// <c>new RudpSession(connector.Channel!, converter, s => new MyHandler(s))</c>.
/// </summary>
/// <remarks>
/// 메시지 채널 경로에는 수신 루프가 없어 원격 끊김을 스스로 감지하지 못한다.
/// 채널이 RUDP 계열(평문 <see cref="RudpMessageChannel"/> 또는 TLS 랩 — 내부 <c>IRudpTransportEvents</c>)이면
/// peer 끊김 통지를 구독해
/// <see cref="Session.Disconnected"/>로 이어 붙인다(원인: <see cref="DisconnectReason.Remote"/> 등).
/// </remarks>
public class RudpSession : Session
{
    /// <param name="channel">연결·수락으로 얻은 메시지 채널. 세션이 소유한다.</param>
    /// <param name="converter">메시지 직렬화기.</param>
    /// <param name="handlerFactory">이 세션의 핸들러 생성 팩토리.</param>
    /// <param name="queueOptions">큐·디스패치 옵션.</param>
    public RudpSession(
        IMessageChannel channel,
        IMessageConverter converter,
        Func<ISession, IMessageHandler> handlerFactory,
        MessageQueueOptions? queueOptions = null)
        : base(channel)
    {
        if (converter is null) throw new ArgumentNullException(nameof(converter));
        if (handlerFactory is null) throw new ArgumentNullException(nameof(handlerFactory));

        // 끊김 통지 구독은 파이프라인 부착 전에 — 부착 직후 수신이 들어와도 통지 경로가 열려 있어야 한다.
        IRudpTransportEvents? transportEvents = channel as IRudpTransportEvents;
        if (transportEvents is not null)
        {
            transportEvents.AddTransportDisconnectedHandler(OnTransportDisconnected);
        }

        AttachPipeline(new MessagePipeline(channel, converter, handlerFactory(this), queueOptions));

        // 구독 전 이미 단절이 발생했으면(수용→세션 생성 창구) 래치에서 회수한다 —
        // 이 경로의 끊김은 이벤트로는 오지 않는다. Session 재생 보장으로 늦은 앱 구독자에게도 전달된다.
        if (transportEvents is not null && transportEvents.TryConsumeLatchedDisconnect(out DisconnectReason latched))
        {
            MarkDisconnected(latched, null);
        }
    }

    private void OnTransportDisconnected(DisconnectReason reason) => MarkDisconnected(reason, null);
}
