using System;
using Communication.Shared.Channels;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;

namespace Communication.Network.TCP;

/// <summary>
/// TCP 세션. 앱이 채널 위에 직접 생성한다 —
/// <c>new TcpSession(connector.Channel!, converter, s => new MyHandler(s))</c>.
/// </summary>
public class TcpSession : Session
{
    /// <param name="channel">연결·수락으로 얻은 채널. 세션이 소유한다.</param>
    /// <param name="converter">메시지 직렬화기.</param>
    /// <param name="handlerFactory">이 세션의 핸들러 생성 팩토리.</param>
    /// <param name="queueOptions">큐·디스패치 옵션.</param>
    public TcpSession(
        IByteChannel channel,
        IMessageConverter converter,
        Func<ISession, IMessageHandler> handlerFactory,
        MessageQueueOptions? queueOptions = null)
        : base(channel)
    {
        if (converter is null) throw new ArgumentNullException(nameof(converter));
        if (handlerFactory is null) throw new ArgumentNullException(nameof(handlerFactory));

        AttachPipeline(new MessagePipeline(channel, converter, handlerFactory(this), queueOptions));
    }
}
