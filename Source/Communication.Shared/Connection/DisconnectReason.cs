namespace Communication.Shared.Connection;

/// <summary>세션 끊김의 원인.</summary>
public enum DisconnectReason
{
    /// <summary>이 쪽의 명시적 <c>Disconnect()</c> 또는 Dispose.</summary>
    Local,

    /// <summary>상대의 닫힘(스트림 끝, peer disconnect).</summary>
    Remote,

    /// <summary>I/O·프로토콜 오류.</summary>
    Error,
}
