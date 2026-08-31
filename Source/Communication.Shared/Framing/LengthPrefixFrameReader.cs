using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;

namespace Communication.Shared.Framing;

/// <summary>
/// <see cref="IByteChannel"/>에서 length-prefix 프레임을 읽는다. 부분 읽기를 누적 처리한다.
/// </summary>
public sealed class LengthPrefixFrameReader
{
    private readonly IByteChannel _channel;
    private readonly byte[] _header = new byte[LengthPrefixFramer.HeaderSize];

    public LengthPrefixFrameReader(IByteChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    /// <summary>
    /// 다음 프레임의 길이를 읽는다.
    /// </summary>
    /// <returns>프레임 길이. 0 = 프레임 경계에서의 정상적인 스트림 끝(원격 닫힘).</returns>
    /// <exception cref="EndOfStreamException">헤더 도중에 스트림이 끊긴 경우(프로토콜 오류).</exception>
    /// <exception cref="InvalidDataException">길이가 음수거나 상한 초과인 경우.</exception>
    public async ValueTask<int> ReadFrameLengthAsync(CancellationToken cancellationToken = default)
    {
        int read = await ReadExactOrZeroAsync(_header.AsMemory(), cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            return 0; // 프레임 경계 EOF
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(_header);
        if (length < 0 || length > LengthPrefixFramer.MaxFrameLength)
        {
            throw new InvalidDataException($"잘못된 프레임 길이: {length}");
        }

        return length;
    }

    /// <summary><paramref name="buffer"/>를 가득 채울 때까지 읽는다. 도중에 끊기면 예외.</summary>
    /// <exception cref="EndOfStreamException">버퍼를 채우기 전에 스트림이 끝난 경우.</exception>
    public async ValueTask ReadExactAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await ReadExactOrZeroAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            throw new EndOfStreamException("프레임 본문 도중에 스트림이 끝났습니다.");
        }
    }

    /// <summary>버퍼를 가득 채운다. 첫 읽기가 0이면(경계 EOF) 0을 반환.</summary>
    private async ValueTask<int> ReadExactOrZeroAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await _channel.ReadAsync(buffer.Slice(total), cancellationToken).ConfigureAwait(false);
            if (n <= 0)
            {
                return total == 0 ? 0 : throw new EndOfStreamException("프레임 도중에 스트림이 끝났습니다.");
            }

            total += n;
        }

        return total;
    }
}
