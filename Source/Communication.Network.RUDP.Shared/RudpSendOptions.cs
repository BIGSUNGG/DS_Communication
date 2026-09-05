using Communication.Shared.Channels;

namespace Communication.Network.RUDP;

/// <summary>
/// RUDP 송신 옵션. <see cref="SendOptions"/> 파생 타입이라 <c>SendAsync(message, options)</c>에
/// 메시지별로 다른 전송 방식을 지정할 수 있다.
/// </summary>
/// <remarks>
/// 불변이며 전송 방식별 공용 인스턴스(<see cref="ReliableOrdered"/> 등)를 제공해
/// 송신 경로에서 옵션 할당이 없도록 한다. 기본값은 <see cref="RudpDeliveryMethod.ReliableOrdered"/>.
/// </remarks>
public sealed class RudpSendOptions : SendOptions
{
    /// <param name="deliveryMethod">이 메시지에 사용할 전송 방식.</param>
    public RudpSendOptions(RudpDeliveryMethod deliveryMethod)
    {
        DeliveryMethod = deliveryMethod;
    }

    /// <summary>이 메시지의 패킷 전송 방식.</summary>
    public RudpDeliveryMethod DeliveryMethod { get; }

    /// <summary><see cref="RudpDeliveryMethod.ReliableOrdered"/> 공용 인스턴스.</summary>
    public static RudpSendOptions ReliableOrdered { get; } = new(RudpDeliveryMethod.ReliableOrdered);

    /// <summary><see cref="RudpDeliveryMethod.ReliableUnordered"/> 공용 인스턴스.</summary>
    public static RudpSendOptions ReliableUnordered { get; } = new(RudpDeliveryMethod.ReliableUnordered);

    /// <summary><see cref="RudpDeliveryMethod.Sequenced"/> 공용 인스턴스.</summary>
    public static RudpSendOptions Sequenced { get; } = new(RudpDeliveryMethod.Sequenced);

    /// <summary><see cref="RudpDeliveryMethod.ReliableSequenced"/> 공용 인스턴스.</summary>
    public static RudpSendOptions ReliableSequenced { get; } = new(RudpDeliveryMethod.ReliableSequenced);

    /// <summary><see cref="RudpDeliveryMethod.Unreliable"/> 공용 인스턴스.</summary>
    public static RudpSendOptions Unreliable { get; } = new(RudpDeliveryMethod.Unreliable);
}
