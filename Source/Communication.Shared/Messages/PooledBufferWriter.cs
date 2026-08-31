using System;
using System.Buffers;

namespace Communication.Shared.Messages;

/// <summary>
/// ArrayPool 기반 성장 <see cref="IBufferWriter{T}"/>. 파이프라인의 직렬화·coalesce 배치용.
/// </summary>
internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _written;

    public PooledBufferWriter(int initialCapacity = 4096)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 16));
    }

    /// <summary>현재까지 쓰인 영역.</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    /// <summary>현재까지 쓰인 영역(스팬).</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    /// <summary>쓰인 영역을 되쓰기용(길이 헤더 기입 등)으로 노출한다. 파이프라인 내부 전용.</summary>
    public Span<byte> GetWritableSpan() => _buffer.AsSpan(0, _written);

    public int WrittenCount => _written;

    public void Clear() => _written = 0;

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
