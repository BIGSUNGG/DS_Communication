using System;
using Communication.Shared.Connection;

namespace Communication.Network.RUDP;

/// <summary>
/// RUDP 계열 채널의 끊김 통지 내부 계약 — <see cref="RudpMessageChannel"/>(평문)과
/// TLS 랩 채널(암호문) 양쪽이 구현해 <see cref="RudpSession"/>이 구체 타입 없이 구독한다.
/// </summary>
/// <remarks>
/// 이벤트가 아니라 구독 추가 메서드로 계약을 준다 — 세션은 채널 소유자로 생애 주기 내내 구독을 유지하므로
/// 제거는 필요 없고, internal 이벤트의 암시적 인터페이스 구현 제약(public 요구)도 우회한다.
/// </remarks>
internal interface IRudpTransportEvents
{
    /// <summary>peer 끊김 통지 구독을 추가한다. 세션당 1회는 <c>Session</c> 쪽 가드가 보장한다.</summary>
    void AddTransportDisconnectedHandler(Action<DisconnectReason> handler);

    /// <summary>구독 직후 호출 — 구독 전 발생한 단절을 회수한다(이벤트·래치 양쪽 경로로 정확히 1회).</summary>
    bool TryConsumeLatchedDisconnect(out DisconnectReason reason);
}
