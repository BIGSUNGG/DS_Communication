using System;
using System.Buffers;
using System.Buffers.Binary;

namespace Communication.Shared.Framing;

/// <summary>
/// 4바이트 little-endian 길이 + payload 프레이밍의 쓰기 쪽.
/// 읽기 쪽(부분 읽기 처리)은 <see cref="LengthPrefixFrameReader"/>.
/// </summary>
public static class LengthPrefixFramer
{
    /// <summary>헤더(길이 필드) 크기.</summary>
    public const int HeaderSize = 4;

    /// <summary>
    /// 프레임 길이의 절대 상한. 파이프라인 실효 상한은 <c>MessageQueueOptions.MaxFrameLength</c>(기본 4MB)로
    /// 이보다 낮게 조정하며, 이 절대 상한을 넘는 값은 허용되지 않는다.
    /// </summary>
    public const int MaxFrameLength = 64 * 1024 * 1024;

    /// <summary><paramref name="destination"/>에 length-prefix + payload를 쓴다.</summary>
    public static void WriteFrame(IBufferWriter<byte> destination, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxFrameLength)
        {
            throw new ArgumentException($"프레임 길이 {payload.Length}가 상한 {MaxFrameLength}를 초과합니다.", nameof(payload));
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        destination.Write(header);
        destination.Write(payload);
    }
}
