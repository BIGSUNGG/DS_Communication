using System;

namespace Communication.Shared.Connection;

/// <summary><c>ISession.Disconnected</c> 이벤트 인자. 세션당 1회만 전달된다.</summary>
public sealed class DisconnectedEventArgs : EventArgs
{
    public DisconnectedEventArgs(DisconnectReason reason, Exception? exception = null)
    {
        Reason = reason;
        Exception = exception;
    }

    /// <summary>끊김 원인.</summary>
    public DisconnectReason Reason { get; }

    /// <summary><see cref="Reason"/>이 <see cref="DisconnectReason.Error"/>일 때의 원인 예외.
    /// 바이트 채널(TCP) 경로에서는 <see cref="DisconnectReason.Timeout"/>(<see cref="TimeoutException"/>)과
    /// <see cref="DisconnectReason.FlowControl"/>(<see cref="InvalidOperationException"/>)도 원인 예외를 실는다.
    /// 그 외(및 RUDP 전송 끊김 통지)에는 <c>null</c>.</summary>
    public Exception? Exception { get; }
}
