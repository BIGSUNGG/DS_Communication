using System.Buffers.Binary;
using System.Text;
using Communication.Shared.Connection;
using Communication.Shared.Messages;
using Xunit;

namespace Communication.Tests;

public class MessagePipelineTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("조건이 시간 안에 만족되지 않았습니다.");
            }

            await Task.Delay(10);
        }
    }

    private static void FeedFrame(FakeByteChannel channel, string message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message);
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        channel.Feed(header);
        channel.Feed(payload);
    }

    [Fact]
    public async Task Send_WritesLengthPrefixedFrames()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler);
        pipeline.Start();

        await pipeline.SendAndFlushAsync("hello");

        byte[] written = Assert.Single(channel.Writes);
        Assert.Equal(5, BinaryPrimitives.ReadInt32LittleEndian(written));
        Assert.Equal("hello", Encoding.UTF8.GetString(written, 4, written.Length - 4));
    }

    [Fact]
    public async Task Send_CoalescesBatchIntoSingleWrite()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler);

        // 루프 시작 전에 큐잉해 두 메시지가 한 배치로 드레인되게 한다.
        Task first = pipeline.SendAsync("a");
        Task second = pipeline.SendAsync("b");
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        pipeline.Start();

        await WaitUntilAsync(() => channel.Writes.Count >= 1);
        byte[] written = Assert.Single(channel.Writes); // 두 프레임이 한 write로 coalesce
        Assert.Equal(4 + 1 + 4 + 1, written.Length);
    }

    [Fact]
    public async Task Backpressure_WaitsUntilSpaceFrees()
    {
        var channel = new FakeByteChannel();
        channel.BlockWrites();
        var handler = new RecordingHandler();
        var options = new MessageQueueOptions { MaxPendingMessages = 2 };
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler, options);
        pipeline.Start();

        Task t1 = pipeline.SendAsync("1");
        Task t2 = pipeline.SendAsync("2");
        await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(5)); // 큐(슬롯 2) 진입 완료

        Task third = pipeline.SendAsync("3");
        await Assert.ThrowsAsync<TimeoutException>(() => third.WaitAsync(TimeSpan.FromMilliseconds(200)));

        channel.ReleaseWrites(); // wire 완료 → 슬롯 해제
        await third.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SendAfterDispose_Faults()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        var pipeline = new MessagePipeline(channel, new StringConverter(), handler);
        pipeline.Start();
        pipeline.Dispose();

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.SendAsync("x"));
    }

    [Fact]
    public async Task Receive_DispatchesMessages()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler);
        pipeline.Start();

        FeedFrame(channel, "one");
        FeedFrame(channel, "two");

        await WaitUntilAsync(() => handler.Messages.Count == 2);
        Assert.Equal(new object[] { "one", "two" }, handler.Messages);
    }

    [Fact]
    public async Task Receive_Eof_RaisesRemoteDisconnect()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler);

        DisconnectReason? reason = null;
        pipeline.Disconnected += (r, e) => reason = r;
        pipeline.Start();

        FeedFrame(channel, "last");
        channel.Complete();

        await WaitUntilAsync(() => reason != null);
        Assert.Equal(DisconnectReason.Remote, reason);
        await WaitUntilAsync(() => handler.Messages.Count == 1); // 끊김 직전 메시지는 전달됨
    }

    [Fact]
    public async Task Receive_CorruptFrame_RaisesErrorDisconnect()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler);

        DisconnectReason? reason = null;
        Exception? error = null;
        pipeline.Disconnected += (r, e) =>
        {
            reason = r;
            error = e;
        };
        pipeline.Start();

        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, 999); // 본문 없이 긴 길이
        channel.Feed(header);
        channel.Complete();

        await WaitUntilAsync(() => reason != null);
        Assert.Equal(DisconnectReason.Error, reason);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerException_IsIsolated_LoopContinues(bool inlineDispatch)
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler(throwOn: "bad");
        var options = new MessageQueueOptions { InlineDispatch = inlineDispatch };
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler, options);
        pipeline.Start();

        FeedFrame(channel, "bad");
        FeedFrame(channel, "good");

        await WaitUntilAsync(() => handler.Messages.Count == 2);
        Assert.Equal(new object[] { "bad", "good" }, handler.Messages); // 예외 후에도 수신 계속
    }
}
