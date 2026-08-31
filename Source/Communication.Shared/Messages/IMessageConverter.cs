using System;
using System.Buffers;

namespace Communication.Shared.Messages;

/// <summary>
/// 메시지 ↔ 바이트 변환기. 구현은 앱 또는 직렬화 프로젝트(예: DS_MessageProtocol)가 주입한다.
/// </summary>
public interface IMessageConverter
{
    /// <summary>메시지를 <paramref name="writer"/>에 직렬화한다. 송신 힙 할당을 피하기 위해 결과 배열을 반환하지 않는다.</summary>
    void Serialize(object message, IBufferWriter<byte> writer);

    /// <summary>프레임/수신 payload를 메시지로 역직렬화한다. 스팬은 호출 중에만 유효하다.</summary>
    object Deserialize(ReadOnlySpan<byte> message);
}
