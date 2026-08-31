using System;
using System.Buffers;

namespace Communication.Shared.Messages;

/// <summary>
/// ArrayPool 기반 성장 <see cref="IBufferWriter{T}"/>. 파이프라인의 직렬화·coalesce 배치용.
/// </summary>
internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    // 한 번의 대형 배치로 커진 버퍼를 계속 들고 있지 않는 상한 — 초과 시 Clear에서 기본 크기로 재렌탈.
    private const int MaxRetainedBytes = 256 * 1024;

    private readonly int _initialCapacity;
    private byte[] _buffer;
    private int _written;

    public PooledBufferWriter(int initialCapacity = 4096)
    {
        _initialCapacity = Math.Max(initialCapacity, 16);
        _buffer = ArrayPool<byte>.Shared.Rent(_initialCapacity);
    }

    /// <summary>현재까지 쓰인 영역.</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    /// <summary>현재까지 쓰인 영역(스팬).</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    /// <summary>쓰인 영역을 되쓰기용(길이 헤더 기입 등)으로 노출한다. 파이프라인 내부 전용.</summary>
    public Span<byte> GetWritableSpan() => _buffer.AsSpan(0, _written);

    public int WrittenCount => _written;

    /// <summary>쓰인 위치를 지정한 곳으로 되돌린다(부분 직렬화 프레임 폐기용).</summary>
    public void RewindTo(int count)
    {
        if (count < 0 || count > _written)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _written = count;
    }

    public void Clear()
    {
        _written = 0;

        if (_buffer.Length > MaxRetainedBytes)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = ArrayPool<byte>.Shared.Rent(_initialCapacity);
        }
    }

    public void Advance(int count)
    {
        if (count < 0 || _written + count > _buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 1)
        {
            sizeHint = 1;
        }

        if (_written + sizeHint <= _buffer.Length)
        {
            return;
        }

        int target = Math.Max(_buffer.Length * 2, _written + sizeHint);
        byte[] bigger = ArrayPool<byte>.Shared.Rent(target);
        _buffer.AsSpan(0, _written).CopyTo(bigger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = bigger;
    }

    public void Dispose()
    {
        byte[] buffer = _buffer;
        _buffer = Array.Empty<byte>();
        _written = 0;
        if (buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
