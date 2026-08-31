using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Communication.Shared.Channels;

namespace Communication.Shared.Framing;

/// <summary>
/// <see cref="IByteChannel"/>에서 length-prefix 프레임을 읽는다.
/// 단일 누적 버퍼(ArrayPool)에 부분 읽기를 모으고, 완성된 프레임은 버퍼의 제로카피 슬라이스로 돌려준다.
/// </summary>
public sealed class LengthPrefixFrameReader : IDisposable
{
    private const int DefaultBufferSize = 64 * 1024;

    private readonly IByteChannel _channel;
    private byte[] _buffer;
    private int _offset; // 미처리 데이터 시작 인덱스
    private int _end;    // 누적된 마지막 바이트의 다음 인덱스
    private bool _disposed;

    public LengthPrefixFrameReader(IByteChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
    }

    /// <summary>
    /// 다음 프레임의 payload를 읽는다.
    /// </summary>
    /// <returns>
    /// 내부 버퍼의 슬라이스 — 다음 호출 전까지 유효하다(버퍼는 재사용된다).
    /// 빈 메모리 = 프레임 경계에서의 정상적인 스트림 끝(원격 닫힘).
    /// </returns>
    /// <exception cref="EndOfStreamException">프레임 도중에 스트림이 끊긴 경우.</exception>
    /// <exception cref="InvalidDataException">프레임 길이가 0·음수거나 상한 초과인 경우.</exception>
    public async ValueTask<ReadOnlyMemory<byte>> ReadFrameAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LengthPrefixFrameReader));
        }

        // 이전 호출이 돌려준 슬라이스는 여기서 무효화된다 — 앞부분 정리(컴팩트).
        if (_offset > 0)
        {
            int remaining = _end - _offset;
            if (remaining > 0)
            {
                Buffer.BlockCopy(_buffer, _offset, _buffer, 0, remaining);
            }

            _end = remaining;
            _offset = 0;
        }

        while (true)
        {
            if (_end >= LengthPrefixFramer.HeaderSize)
            {
                int length = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(0, LengthPrefixFramer.HeaderSize));
                if (length <= 0)
                {
                    throw new InvalidDataException($"잘못된 프레임 길이: {length}");
                }

                if (length > LengthPrefixFramer.MaxFrameLength)
                {
                    throw new InvalidDataException($"잘못된 프레임 길이: {length} (상한 {LengthPrefixFramer.MaxFrameLength})");
                }

                int frameTotal = LengthPrefixFramer.HeaderSize + length;
                if (_end >= frameTotal)
                {
                    // 프레임 전체가 이미 버퍼에 있음 — 제로카피 슬라이스.
                    _offset = frameTotal;
                    return new ReadOnlyMemory<byte>(_buffer, LengthPrefixFramer.HeaderSize, length);
                }

                // 프레임이 남은 공간보다 큼 — 버퍼 성장(이미 앞부분 정리는 됨).
                EnsureCapacity(frameTotal);
            }

            int read = await _channel.ReadAsync(_buffer.AsMemory(_end), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                if (_end == 0)
                {
                    return ReadOnlyMemory<byte>.Empty; // 프레임 경계 EOF
                }

                throw new EndOfStreamException("프레임 도중에 스트림이 끝났습니다.");
            }

            _end += read;
        }
    }

    /// <summary>전체 프레임을 수용하도록 버퍼를 키운다(2배 성장, ArrayPool 재렌탈).</summary>
    private void EnsureCapacity(int required)
    {
        if (_buffer.Length >= required)
        {
            return;
        }

        int target = Math.Max(_buffer.Length * 2, required);
        byte[] bigger = ArrayPool<byte>.Shared.Rent(target);
        _buffer.AsSpan(0, _end).CopyTo(bigger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = bigger;
    }

    /// <summary>누적 버퍼를 풀에 반환한다.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // 누적 버퍼는 풀에 반납하지 않는다: 수신 루프가 아직 이 버퍼로 ReadAsync 중일 수 있어
        // (취소는 진행 중 I/O를 즉시 중단시키지 않음) 반납 시 다른 컴포넌트가 같은 배열을 쓸 수 있다. GC에 맡긴다.
        _buffer = Array.Empty<byte>();
    }
}
