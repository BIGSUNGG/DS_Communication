using System;
using System.Threading;
using System.Threading.Tasks;

namespace Communication.Shared.Channels;

/// <summary>
/// 바이트 스트림 채널. 메시지 경계는 제공하지 않으며, 프레이밍은 상위(<c>MessagePipeline</c> + Framer)가 담당한다.
/// </summary>
public interface IByteChannel : IDisposable
{
    /// <summary>전송 계층 관점의 연결 상태. 끊김 감지의 보조 신호이며 최종 판단은 세션이 한다.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// 버퍼에 바이트를 읽는다. 반환 0 = 스트림 끝(원격 닫힘).
    /// </summary>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>버퍼 전체를 스트림에 쓴다.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
}
