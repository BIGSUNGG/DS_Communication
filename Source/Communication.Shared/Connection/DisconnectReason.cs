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

    /// <summary>수신 프레임 완료 마감 초과(읽기 유휴 타임아웃).</summary>
    Timeout,

    /// <summary>흐름 제어 — 수신 처리가 밀려 선언된 백프레셔 상한을 초과(실패 폐쇄 단절).</summary>
    FlowControl,
}
