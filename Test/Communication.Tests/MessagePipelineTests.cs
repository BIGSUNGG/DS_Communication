using System.Buffers.Binary;
using System.IO;
using System.Text;
using Communication.Shared.Channels;
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

    [Fact]
    public void Start_Twice_Throws()
    {
        var channel = new FakeByteChannel();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), new RecordingHandler());
        pipeline.Start();

        Assert.Throws<InvalidOperationException>(() => pipeline.Start());
    }

    [Fact]
    public async Task ThrowingConverter_FaultsFlushAndRaisesErrorDisconnect()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new ThrowingConverter(), handler);

        DisconnectReason? reason = null;
        pipeline.Disconnected += (r, e) => reason = r;
        pipeline.Start();

        // 직렬화 예외 → 해당 flush fault 후 Error 끊김으로 격상.
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.SendAndFlushAsync("x"));

        await WaitUntilAsync(() => reason != null);
        Assert.Equal(DisconnectReason.Error, reason);
    }

    [Fact]
    public async Task Send_EmptyPayload_FaultsFlushAndRaisesErrorDisconnect()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new EmptyConverter(), handler);

        DisconnectReason? reason = null;
        pipeline.Disconnected += (r, e) => reason = r;
        pipeline.Start();

        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.SendAndFlushAsync("x"));

        await WaitUntilAsync(() => reason != null);
        Assert.Equal(DisconnectReason.Error, reason);
    }

    [Fact]
    public async Task MessageChannel_SendEmptyPayload_FaultsFlushAndRaisesErrorDisconnect()
    {
        var channel = new FakeMessageChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new EmptyConverter(), handler);

        DisconnectReason? reason = null;
        pipeline.Disconnected += (r, e) => reason = r;
        pipeline.Start();

        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.SendAndFlushAsync("x"));

        await WaitUntilAsync(() => reason != null);
        Assert.Equal(DisconnectReason.Error, reason);
        Assert.Empty(channel.Sent); // 빈 페이로드는 채널까지 가지 않는다.
    }

    [Fact]
    public async Task Dispose_WhileWriteGated_FaultsPendingFlush()
    {
        var channel = new FakeByteChannel();
        channel.BlockWrites();
        var handler = new RecordingHandler();
        var pipeline = new MessagePipeline(channel, new StringConverter(), handler);
        pipeline.Start();

        Task flush = pipeline.SendAndFlushAsync("x");
        await channel.WriteEntered.WaitAsync(TimeSpan.FromSeconds(5)); // 쓰기가 게이트에 막힌 상태 확정
        pipeline.Dispose(); // Cancel → 쓰기가 취소되며 배치 전체의 flush가 fault

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush);
    }

    [Fact]
    public async Task SendAndFlush_HonorsCancellationToken()
    {
        var channel = new FakeByteChannel();
        channel.BlockWrites();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler);
        pipeline.Start();

        using var cts = new CancellationTokenSource();
        Task flush = pipeline.SendAndFlushAsync("x", cancellationToken: cts.Token);
        await channel.WriteEntered.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush);
    }

    [Fact]
    public async Task Receive_ZeroLengthFrame_RaisesErrorDisconnect()
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

        channel.Feed(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // 길이 0 프레임 = 프로토콜 위반

        await WaitUntilAsync(() => reason != null);
        Assert.Equal(DisconnectReason.Error, reason);
        Assert.IsType<InvalidDataException>(error);
    }

    [Fact]
    public async Task Disconnect_StopsStandalonePipeline_FurtherSendsFault()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler);

        DisconnectReason? reason = null;
        pipeline.Disconnected += (r, e) => reason = r;
        pipeline.Start();

        channel.Complete(); // Remote EOF → 통지 후 파이프라인 자체 정지
        await WaitUntilAsync(() => reason != null);

        await WaitUntilAsync(() => pipeline.SendAsync("x").IsFaulted); // 정지 후 송신은 fault(완료 태스크)
    }

    [Fact]
    public async Task MessageChannel_SendAndFlush_RoundTripsWithOptions()
    {
        var channel = new FakeMessageChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler);
        pipeline.Start();

        var options = new TestSendOptions();
        await pipeline.SendAndFlushAsync("hello", options);

        (byte[] Payload, SendOptions? Options) sent = Assert.Single(channel.Sent);
        Assert.Equal("hello", Encoding.UTF8.GetString(sent.Payload));
        Assert.Same(options, sent.Options); // 옵션이 채널까지 그대로 전달됨
    }

    [Fact]
    public async Task MessageChannel_SendFailure_RaisesErrorDisconnect()
    {
        var channel = new FakeMessageChannel();
        channel.FailSend(new IOException("channel down"));
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler);

        DisconnectReason? reason = null;
        pipeline.Disconnected += (r, e) => reason = r;
        pipeline.Start();

        await pipeline.SendAsync("x"); // 송신 루프에서 실패 → Error 끊김

        await WaitUntilAsync(() => reason != null);
        Assert.Equal(DisconnectReason.Error, reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MessageChannel_Receive_Dispatches(bool inlineDispatch)
    {
        var channel = new FakeMessageChannel();
        var handler = new RecordingHandler();
        var options = new MessageQueueOptions { InlineDispatch = inlineDispatch };
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler, options);
        pipeline.Start();

        channel.RaiseReceived(Encoding.UTF8.GetBytes("one"));
        channel.RaiseReceived(Encoding.UTF8.GetBytes("two"));

        await WaitUntilAsync(() => handler.Messages.Count == 2);
        Assert.Equal(new object[] { "one", "two" }, handler.Messages);
    }
}
