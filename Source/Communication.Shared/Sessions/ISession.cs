using System;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;
using Communication.Shared.Connection;

namespace Communication.Shared.Sessions;

/// <summary>
/// 연결형 메시지 세션의 공개 계약. 앱이 생성하고, 끊김 관측은 <see cref="Disconnected"/> 이벤트만 사용한다.
/// </summary>
public interface ISession : IDisposable
{
    /// <summary>메시지를 송신 큐에 넣는다. 큐잉만 완료되면 반환되는 fire-and-forget 경로.</summary>
    Task SendAsync(object message);

    /// <summary>메시지를 송신 큐에 넣는다. <paramref name="options"/>는 전송 구현이 해석한다.</summary>
    Task SendAsync(object message, SendOptions? options);

    /// <summary>큐잉 후 와이어 기록 완료까지 기다린다.</summary>
    Task SendAndFlushAsync(object message, SendOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>로컬 주도로 끊는다. <c>Disconnected(Local)</c>이 1회 통지된다.</summary>
    void Disconnect();

    /// <summary>로컬 끊김 플래그와 전송 상태를 함께 본 연결 여부.</summary>
    bool IsConnected();

    /// <summary>끊김 통지. 원인(<see cref="DisconnectReason"/>)과 함께 세션당 1회만 발생한다. 재접속 이벤트는 없다.</summary>
    event EventHandler<DisconnectedEventArgs>? Disconnected;
}
