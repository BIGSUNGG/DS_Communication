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
/// 버퍼는 선언된 프레임 길이가 아니라 실제 누적된 데이터 기준으로만 성장한다 —
/// 헤더만으로 거대 버퍼를 선할당하는 메모리 증폭 공격을 막는다.
/// </summary>
public sealed class LengthPrefixFrameReader : IDisposable
{
    private const int DefaultBufferSize = 64 * 1024;

    private readonly IByteChannel _channel;
    private readonly TimeSpan? _frameTimeout;
    private readonly int _maxFrameLength;
    private byte[] _buffer;
    private int _offset; // 미처리 데이터 시작 인덱스
    private int _end;    // 누적된 마지막 바이트의 다음 인덱스
    private bool _disposed;

    /// <param name="channel">읽을 바이트 채널.</param>
    /// <param name="frameTimeout">
    /// 프레임 완료 마감. 프레임의 첫 바이트가 도착한 순간 시작되어 이 시간 안에 프레임이 완성되지 않으면
    /// <see cref="TimeoutException"/>을 던진다. <c>null</c> 또는 0 이하면 비활성화.
    /// </param>
    /// <param name="maxFrameLength">
    /// 허용 프레임 길이 상한. 초과 프레임은 <see cref="System.IO.InvalidDataException"/>으로 거부된다.
    /// 기본은 절대 상한(<see cref="LengthPrefixFramer.MaxFrameLength"/>) — 파이프라인은 옵션으로 더 낮은 값을 전달한다.
    /// </param>
    public LengthPrefixFrameReader(IByteChannel channel, TimeSpan? frameTimeout = null, int maxFrameLength = LengthPrefixFramer.MaxFrameLength)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _frameTimeout = frameTimeout is { } t && t > TimeSpan.Zero ? t : null;
        if (maxFrameLength <= 0 || maxFrameLength > LengthPrefixFramer.MaxFrameLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameLength));
        }

        _maxFrameLength = maxFrameLength;
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
    /// <exception cref="TimeoutException">프레임 완료 마감 활성화 시, 마감 안에 프레임이 완성되지 않은 경우.</exception>
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

        CancellationTokenSource? deadlineCts = null;
        try
        {
            while (true)
            {
                if (_end >= LengthPrefixFramer.HeaderSize)
                {
                    int length = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(0, LengthPrefixFramer.HeaderSize));
                    if (length <= 0)
                    {
                        throw new InvalidDataException($"잘못된 프레임 길이: {length}");
                    }

                    if (length > _maxFrameLength)
                    {
                        throw new InvalidDataException($"잘못된 프레임 길이: {length} (상한 {_maxFrameLength})");
                    }

                    int frameTotal = LengthPrefixFramer.HeaderSize + length;
                    if (_end >= frameTotal)
                    {
                        // 프레임 전체가 이미 버퍼에 있음 — 제로카피 슬라이스.
                        _offset = frameTotal;
                        return new ReadOnlyMemory<byte>(_buffer, LengthPrefixFramer.HeaderSize, length);
                    }

                    // 버퍼가 가득 찼을 때만 2배 성장 — 선언 길이 기준 사전 할당은 하지 않는다.
                    // (헤더만 보내고 본문을 흘려보내는 느린 증폭 공격에서 메모리를 누적량에 묶는다.)
                    if (_end >= _buffer.Length)
                    {
                        EnsureCapacity(_buffer.Length + 1);
                    }
                }

                // 프레임 마감 — 첫 바이트가 도착한(_end > 0) 시점부터 한 번만 시작한다.
                // 바이트가 전혀 없는 완전 유휴 연결은 마감 대상이 아니다(하트비트는 앱 책임).
                CancellationToken readToken = cancellationToken;
                if (_frameTimeout.HasValue && _end > 0)
                {
                    if (deadlineCts is null)
                    {
                        deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        deadlineCts.CancelAfter(_frameTimeout.Value);
                    }

                    readToken = deadlineCts.Token;
                }

                int read;
                try
                {
                    read = await _channel.ReadAsync(_buffer.AsMemory(_end), readToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (deadlineCts is not null && deadlineCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"프레임 수신이 {_frameTimeout!.Value} 안에 완료되지 않았습니다.");
                }

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
        finally
        {
            deadlineCts?.Dispose();
        }
    }

    /// <summary>버퍼를 키운다(2배 성장, ArrayPool 재렌탈).</summary>
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

    /// <summary>테스트 전용 — 현재 누적 버퍼 용량.</summary>
    internal int BufferCapacity => _buffer.Length;

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
