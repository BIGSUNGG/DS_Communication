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

    /// <summary><see cref="Reason"/>이 <see cref="DisconnectReason.Error"/>일 때의 원인 예외. 그 외에는 <c>null</c>.</summary>
    public Exception? Exception { get; }
}
