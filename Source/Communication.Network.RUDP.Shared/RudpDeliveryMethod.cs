namespace Communication.Network.RUDP;

/// <summary>
/// RUDP 패킷 전송 방식. 메시지별로 다르게 지정할 수 있다(<see cref="RudpSendOptions"/>).
/// 값은 내부적으로 LiteNetLib <c>DeliveryMethod</c>로 1:1 매핑되지만, 이 열거형 자체가 앱 공개면이다.
/// </summary>
public enum RudpDeliveryMethod
{
    /// <summary>
    /// Unreliable. Packets can be dropped, can be duplicated, can arrive without order.
    /// </summary>
    Unreliable = 4,

    /// <summary>
    /// Reliable. Packets won't be dropped, won't be duplicated, can arrive without order.
    /// </summary>
    ReliableUnordered = 0,

    /// <summary>
    /// Unreliable. Packets can be dropped, won't be duplicated, will arrive in order.
    /// </summary>
    Sequenced = 1,

    /// <summary>
    /// Reliable and ordered. Packets won't be dropped, won't be duplicated, will arrive in order.
    /// </summary>
    ReliableOrdered = 2,

    /// <summary>
    /// Reliable only last packet. Packets can be dropped (except the last one), won't be duplicated,
    /// will arrive in order. Cannot be fragmented.
    /// </summary>
    ReliableSequenced = 3,
}
