using System;
using System.Threading;
using System.Threading.Tasks;

namespace Communication.Shared.Channels;

/// <summary>
/// 메시지 단위 채널. 전송 계층이 메시지 경계를 제공하므로 프레이머가 필요 없다. (예: RUDP)
/// </summary>
public interface IMessageChannel : IDisposable
{
    /// <summary>전송 계층 관점의 연결 상태.</summary>
    bool IsConnected { get; }

    /// <summary>payload 하나를 보낸다. <paramref name="options"/>는 구현이 해석한다(예: 신뢰성).</summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> payload, SendOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 수신 메시지 알림. 호출 스레드(콜백/펌프)는 구현이 결정한다.
    /// 전달되는 payload는 콜백 안에서만 유효하다.
    /// </summary>
    event Action<ReadOnlyMemory<byte>>? MessageReceived;
}
