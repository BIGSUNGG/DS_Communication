namespace Communication.Shared.Channels;

/// <summary>
/// 송신 부가 옵션의 기반 마커 타입. 필드가 없어 기본 송신은 할당이 없다.
/// 스택별 옵션은 파생 타입으로 확장한다(예: <c>RudpSendOptions</c>).
/// </summary>
public class SendOptions
{
    /// <summary>기본 옵션 인스턴스. 옵션 없이 보낼 때 공용으로 재사용한다.</summary>
    public static SendOptions Default { get; } = new();
}
