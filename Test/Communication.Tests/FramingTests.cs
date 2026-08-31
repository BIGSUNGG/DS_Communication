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
    public async Task LargeDeclaredFrame_PartialArrival_BufferStaysNearAccumulated()
    {
        // 증폭 공격 시나리오: 선언 길이 64MB 헤더만 보내고 본문을 거의 흘려보내지 않는다.
        // 버퍼는 선언 길이가 아니라 누적량 기준으로만 성장해야 한다.
        var channel = new FakeByteChannel();
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, LengthPrefixFramer.MaxFrameLength); // 64MB
        channel.Feed(header);
        channel.Feed(new byte[] { 0x01, 0x02, 0x03, 0x04 }); // 본문 4바이트만 도착

        using var reader = new LengthPrefixFrameReader(channel);
        ValueTask<ReadOnlyMemory<byte>> pending = reader.ReadFrameAsync();

        // 프레임 미완성 — 읽기는 아직 대기 중이어야 한다.
        await Task.Delay(50);
        Assert.False(pending.IsCompleted);
        Assert.True(
            reader.BufferCapacity < 1024 * 1024,
            $"버퍼 {reader.BufferCapacity}바이트 — 선언 길이 기준 사전 할당 의심");

        // 스트림 종료로 풀어준다 — 미완성 프레임이므로 EndOfStream.
        channel.Complete();
        await Assert.ThrowsAsync<EndOfStreamException>(() => pending.AsTask());
    }

    [Fact]
    public async Task LargeFrame_IncrementalGrowth_StillRoundTrips()
    {
        // 누적량 기준 성장으로 바꾼 뒤에도 기본 버퍼(64KB)를 여러 배 넘는 프레임이
        // 성장 경로를 타고 정상 재조립되어야 한다.
        var channel = new FakeByteChannel();
        byte[] payload = new byte[512 * 1024];
        new Random(7).NextBytes(payload);
        FeedFrame(channel, payload);

        using var reader = new LengthPrefixFrameReader(channel);
        ReadOnlyMemory<byte> frame = await reader.ReadFrameAsync();
        Assert.Equal(payload, frame.ToArray());
        Assert.True(reader.BufferCapacity <= 2 * (payload.Length + 4), "누적량 기준 성장 상한(2배) 초과");
    }

    [Fact]
    public async Task PartialFrame_ExceedingFrameTimeout_ThrowsTimeout()
    {
        // 슬로로리스 시나리오: 헤더 + 일부 본문만 보내고 멈춘다.
        var channel = new FakeByteChannel();
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, 100);
        channel.Feed(header);
        channel.Feed(new byte[] { 0x01, 0x02, 0x03, 0x04 });

        using var reader = new LengthPrefixFrameReader(channel, frameTimeout: TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAsync<TimeoutException>(() => reader.ReadFrameAsync().AsTask());
    }

    [Fact]
    public async Task IdleConnection_IsNotSubjectToFrameTimeout()
    {
        // 바이트가 전혀 없는 완전 유휴 연결은 마감 대상이 아니다 — 타임아웃보다 늦게 와도 프레임이 완성된다.
        var channel = new FakeByteChannel();
        using var reader = new LengthPrefixFrameReader(channel, frameTimeout: TimeSpan.FromMilliseconds(150));

        Task<ReadOnlyMemory<byte>> pending = reader.ReadFrameAsync().AsTask();
        await Task.Delay(300); // 마감 시간을 넘긴 유휴 상태 — 아직 끊기지 않아야 한다.
        Assert.False(pending.IsCompleted);

        FeedFrame(channel, "hi"u8.ToArray());
        ReadOnlyMemory<byte> frame = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("hi"u8.ToArray(), frame.ToArray());
    }

    [Fact]
    public async Task FrameTimeoutDisabled_PartialFrame_EndsOnlyOnStreamClose()
    {
        // 비활성화(null)면 부분 프레임이 무기한 유지되고, 종료는 스트림 닫힘으로만 일어난다.
        var channel = new FakeByteChannel();
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, 10);
        channel.Feed(header);
        channel.Feed(new byte[] { 0x01, 0x02 });

        using var reader = new LengthPrefixFrameReader(channel, frameTimeout: null);
        Task<ReadOnlyMemory<byte>> pending = reader.ReadFrameAsync().AsTask();
        await Task.Delay(250);
        Assert.False(pending.IsCompleted);

        channel.Complete();
        await Assert.ThrowsAsync<EndOfStreamException>(() => pending);
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
