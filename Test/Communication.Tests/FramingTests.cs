using System.Buffers.Binary;
using System.IO;
using Communication.Shared.Framing;
using Xunit;

namespace Communication.Tests;

public class FramingTests
{
    [Fact]
    public async Task FrameRoundTrip_WithOneByteReads()
    {
        var channel = new FakeByteChannel();
        byte[] payload = "hello"u8.ToArray();

        // little-endian 길이 + payload를 공급
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        channel.Feed(header);
        channel.Feed(payload);

        var reader = new LengthPrefixFrameReader(channel);
        int length = await reader.ReadFrameLengthAsync();
        Assert.Equal(payload.Length, length);

        byte[] body = new byte[length];
        await reader.ReadExactAsync(body);
        Assert.Equal(payload, body);
    }

    [Fact]
    public async Task ReadFrameLength_AtCleanEof_ReturnsZero()
    {
        var channel = new FakeByteChannel();
        channel.Complete();

        var reader = new LengthPrefixFrameReader(channel);
        Assert.Equal(0, await reader.ReadFrameLengthAsync());
    }

    [Fact]
    public async Task ReadFrameLength_EofMidHeader_Throws()
    {
        var channel = new FakeByteChannel();
        channel.Feed(new byte[] { 0x01, 0x00 }); // 헤더 도중 끊김
        channel.Complete();

        var reader = new LengthPrefixFrameReader(channel);
        await Assert.ThrowsAsync<EndOfStreamException>(() => reader.ReadFrameLengthAsync().AsTask());
    }

    [Fact]
    public async Task ReadExact_EofMidBody_Throws()
    {
        var channel = new FakeByteChannel();
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, 10);
        channel.Feed(header);
        channel.Feed(new byte[] { 0x01, 0x02 }); // 본문 도중 끊김
        channel.Complete();

        var reader = new LengthPrefixFrameReader(channel);
        int length = await reader.ReadFrameLengthAsync();
        await Assert.ThrowsAsync<EndOfStreamException>(() => reader.ReadExactAsync(new byte[length]).AsTask());
    }

    [Fact]
    public async Task NegativeLength_Throws()
    {
        var channel = new FakeByteChannel();
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, -1);
        channel.Feed(header);

        var reader = new LengthPrefixFrameReader(channel);
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadFrameLengthAsync().AsTask());
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
