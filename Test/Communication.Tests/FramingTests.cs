using System.Buffers.Binary;
using System.IO;
using Communication.Shared.Framing;
using Xunit;

namespace Communication.Tests;

public class FramingTests
{
    private static void FeedFrame(FakeByteChannel channel, ReadOnlySpan<byte> payload)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        channel.Feed(header);
        channel.Feed(payload);
    }

    [Fact]
    public async Task FrameRoundTrip_WithOneByteReads()
    {
        var channel = new FakeByteChannel();
        byte[] payload = "hello"u8.ToArray();
        FeedFrame(channel, payload);

        using var reader = new LengthPrefixFrameReader(channel);
        ReadOnlyMemory<byte> frame = await reader.ReadFrameAsync();
        Assert.Equal(payload, frame.ToArray());
    }

    [Fact]
    public async Task TwoFramesInBuffer_SlicedConsecutively()
    {
        var channel = new FakeByteChannel();
        FeedFrame(channel, "one"u8);
        FeedFrame(channel, "two"u8);
        channel.Complete();

        using var reader = new LengthPrefixFrameReader(channel);
        Assert.Equal("one"u8.ToArray(), (await reader.ReadFrameAsync()).ToArray());
        Assert.Equal("two"u8.ToArray(), (await reader.ReadFrameAsync()).ToArray());
        Assert.True((await reader.ReadFrameAsync()).IsEmpty); // 프레임 경계 EOF
    }

    [Fact]
    public async Task FrameLargerThanBuffer_GrowsAndRoundTrips()
    {
        var channel = new FakeByteChannel();
        byte[] payload = new byte[64 * 1024 + 100]; // 기본 버퍼(64KB) 초과 → 성장 경로
        new Random(42).NextBytes(payload);
        FeedFrame(channel, payload);

        using var reader = new LengthPrefixFrameReader(channel);
        ReadOnlyMemory<byte> frame = await reader.ReadFrameAsync();
        Assert.Equal(payload, frame.ToArray());
    }

    [Fact]
    public async Task ReadFrame_AtCleanEof_ReturnsEmpty()
    {
        var channel = new FakeByteChannel();
        channel.Complete();

        using var reader = new LengthPrefixFrameReader(channel);
        Assert.True((await reader.ReadFrameAsync()).IsEmpty);
    }

    [Fact]
    public async Task EofMidHeader_Throws()
    {
        var channel = new FakeByteChannel();
        channel.Feed(new byte[] { 0x01, 0x00 }); // 헤더 도중 끊김
        channel.Complete();

        using var reader = new LengthPrefixFrameReader(channel);
        await Assert.ThrowsAsync<EndOfStreamException>(() => reader.ReadFrameAsync().AsTask());
    }

    [Fact]
    public async Task EofMidBody_Throws()
    {
        var channel = new FakeByteChannel();
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, 10);
        channel.Feed(header);
        channel.Feed(new byte[] { 0x01, 0x02 }); // 본문 도중 끊김
        channel.Complete();

        using var reader = new LengthPrefixFrameReader(channel);
        await Assert.ThrowsAsync<EndOfStreamException>(() => reader.ReadFrameAsync().AsTask());
    }

    [Fact]
    public async Task ZeroLengthFrame_Throws()
    {
        var channel = new FakeByteChannel();
        channel.Feed(new byte[] { 0x00, 0x00, 0x00, 0x00 });

        using var reader = new LengthPrefixFrameReader(channel);
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadFrameAsync().AsTask());
    }

    [Fact]
    public async Task NegativeLength_Throws()
    {
        var channel = new FakeByteChannel();
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, -1);
        channel.Feed(header);

        using var reader = new LengthPrefixFrameReader(channel);
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadFrameAsync().AsTask());
    }

    [Fact]
    public async Task OverLimitLength_Throws()
    {
        var channel = new FakeByteChannel();
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, LengthPrefixFramer.MaxFrameLength + 1);
        channel.Feed(header);

        using var reader = new LengthPrefixFrameReader(channel);
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadFrameAsync().AsTask());
    }

    [Fact]
    public void WriteFrame_ProducesLittleEndianLengthPrefix()
    {
        var writer = new System.Buffers.ArrayBufferWriter<byte>();
        byte[] payload = "abc"u8.ToArray();

        LengthPrefixFramer.WriteFrame(writer, payload);

        Assert.Equal(4 + payload.Length, writer.WrittenCount);
        Assert.Equal(payload.Length, BinaryPrimitives.ReadInt32LittleEndian(writer.WrittenSpan));
        Assert.Equal(payload, writer.WrittenSpan.Slice(4).ToArray());
    }

    [Fact]
    public void WriteFrame_OverLimit_Throws()
    {
        var writer = new System.Buffers.ArrayBufferWriter<byte>();
        byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(LengthPrefixFramer.MaxFrameLength + 1);
        try
        {
            Assert.Throws<ArgumentException>(
                () => LengthPrefixFramer.WriteFrame(writer, rented.AsSpan(0, LengthPrefixFramer.MaxFrameLength + 1)));
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
