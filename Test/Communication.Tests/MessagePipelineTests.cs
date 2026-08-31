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
    public async Task SerializeFailure_FaultsFlushOnly_PipelineStaysConnected()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new SelectiveThrowingConverter(throwOn: "bad"), handler);

        DisconnectReason? reason = null;
        pipeline.Disconnected += (r, e) => reason = r;
        pipeline.Start();

        // 직렬화 예외 → 해당 항목의 flush만 fault, 연결은 유지.
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.SendAndFlushAsync("bad"));

        await pipeline.SendAndFlushAsync("good"); // 이후 메시지는 정상 송신.
        await Task.Delay(50); // 뒤늦은 끊김 통지가 없는지 흡수.

        Assert.Null(reason); // 직렬화 실패는 끊김으로 격상되지 않는다.
        byte[] write = Assert.Single(channel.Writes); // 실패한 항목의 바이트는 와이어에 없음.
        Assert.Equal("good", Encoding.UTF8.GetString(write.AsSpan(4))); // 4바이트 길이 헤더 이후.
    }

    [Fact]
    public async Task SerializeFailure_MidBatch_RewindsOnlyFailedFrame()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new SelectiveThrowingConverter(throwOn: "bad"), handler);

        // Start 전에 큐잉해 세 항목이 단일 코얼리스 배치로 드레인 — "bad"는 배치 중간(frameStart > 0)에서 실패.
        Task good = pipeline.SendAndFlushAsync("good");
        Task bad = pipeline.SendAndFlushAsync("bad");
        Task good2 = pipeline.SendAndFlushAsync("good2");
        pipeline.Start();

        await Assert.ThrowsAsync<InvalidOperationException>(() => bad.WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.WhenAll(good, good2).WaitAsync(TimeSpan.FromSeconds(5)); // 앞뒤 항목 flush는 정상 완료

        // 실패 프레임만 부분 되감기 — 단일 write에 "bad" 앞뒤 프레임이 순서대로 정확히 담긴다.
        byte[] written = Assert.Single(channel.Writes);
        int firstLength = BinaryPrimitives.ReadInt32LittleEndian(written);
        Assert.Equal("good", Encoding.UTF8.GetString(written, 4, firstLength));
        int secondLength = BinaryPrimitives.ReadInt32LittleEndian(written.AsSpan(4 + firstLength));
        Assert.Equal("good2", Encoding.UTF8.GetString(written, 4 + firstLength + 4, secondLength));
    }

    [Fact]
    public async Task SendAndFlush_Completes_WhenDisposedDuringChannelWrite()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler);
        channel.OnWrite = () => pipeline.Dispose(); // 쓰기 직후·슬롯 해제 전 Dispose 경쟁 창 재현
        pipeline.Start();

        // Flush 완료가 슬롯 해제보다 먼저라, Dispose가 슬롯을 정리해도 호출자는 hang하지 않는다.
        await pipeline.SendAndFlushAsync("hello").WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SendAndFlush_MessageChannel_Completes_WhenDisposedDuringSend()
    {
        var channel = new FakeMessageChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler);
        channel.OnSend = () => pipeline.Dispose(); // 송신 직후·슬롯 해제 전 Dispose 경쟁 창 재현
        pipeline.Start();

        await pipeline.SendAndFlushAsync("hello").WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Send_EmptyPayload_FaultsFlushOnly_PipelineStaysConnected()
    {
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new EmptyConverter(), handler);

        DisconnectReason? reason = null;
        pipeline.Disconnected += (r, e) => reason = r;
        pipeline.Start();

        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.SendAndFlushAsync("x"));
        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.SendAndFlushAsync("y")); // 루프 생존 — 다음 항목도 처리됨.
        await Task.Delay(50);

        Assert.Null(reason);
        Assert.Empty(channel.Writes);
    }

    [Fact]
    public async Task SerializeFailure_ReleasesSlot_BackPressureNotShrunk()
    {
        var channel = new FakeByteChannel();
        var options = new MessageQueueOptions { MaxPendingMessages = 1 };
        using var pipeline = new MessagePipeline(channel, new SelectiveThrowingConverter(throwOn: "bad"), new RecordingHandler(), options);
        pipeline.Start();

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.SendAndFlushAsync("bad"));

        // 격리된 항목의 슬롯이 반환되어야 다음 큐잉이 완료된다 (미반환 시 타임아웃).
        Task second = pipeline.SendAsync("good");
        await WaitUntilAsync(() => second.IsCompleted);
    }

    [Fact]
    public async Task MessageChannel_SerializeFailure_FaultsFlushOnly_PipelineStaysConnected()
    {
        var channel = new FakeMessageChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new SelectiveThrowingConverter(throwOn: "bad"), handler);

        DisconnectReason? reason = null;
        pipeline.Disconnected += (r, e) => reason = r;
        pipeline.Start();

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.SendAndFlushAsync("bad"));

        await pipeline.SendAndFlushAsync("good");
        await Task.Delay(50);

        Assert.Null(reason);
        (byte[] Payload, SendOptions? Options) sent = Assert.Single(channel.Sent);
        Assert.Equal("good", Encoding.UTF8.GetString(sent.Payload));
    }

    [Fact]
    public async Task MessageChannel_SendEmptyPayload_FaultsFlushOnly_PipelineStaysConnected()
    {
        var channel = new FakeMessageChannel();
        var handler = new RecordingHandler();
        using var pipeline = new MessagePipeline(channel, new EmptyConverter(), handler);

        DisconnectReason? reason = null;
        pipeline.Disconnected += (r, e) => reason = r;
        pipeline.Start();

        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.SendAndFlushAsync("x"));
        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.SendAndFlushAsync("y")); // 루프 생존.
        await Task.Delay(50);

        Assert.Null(reason);
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
    public async Task SendAndFlush_PrecancelledToken_DoesNotEnqueue_ReturnsCanceled()
    {
        var channel = new FakeByteChannel();
        using var pipeline = new MessagePipeline(channel, new StringConverter(), new RecordingHandler());
        pipeline.Start();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // 이미 취소된 토큰 — 큐잉 없이 즉시 취소 완료.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.SendAndFlushAsync("x", cancellationToken: cts.Token));

        await Task.Delay(50); // 큐에 들어갔다면 송신 루프가 기록했을 시간.
        Assert.Empty(channel.Writes);

        // 큐에 잔류하지 않음 — 이후 정상 송신 하나가 유일한 기록으로 남는다.
        await pipeline.SendAndFlushAsync("y");
        byte[] write = Assert.Single(channel.Writes);
        Assert.Equal("y", Encoding.UTF8.GetString(write.AsSpan(4)));
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

    [Fact]
    public async Task DripFeed_SlowBytes_DisconnectsWithTimeoutReason()
    {
        // 드립 공급(끊임없이 조금씩 바이트)이라도 프레임이 마감 안에 완성되지 않으면 단절된다.
        var channel = new FakeByteChannel();
        var handler = new RecordingHandler();
        var options = new MessageQueueOptions { FrameTimeout = TimeSpan.FromMilliseconds(200) };
        using var pipeline = new MessagePipeline(channel, new StringConverter(), handler, options);

        DisconnectReason? reason = null;
        Exception? error = null;
        pipeline.Disconnected += (r, e) =>
        {
            reason = r;
            error = e;
        };
        pipeline.Start();

        // 완성되지 않을 프레임 선언 후 50ms마다 1바이트씩 드립.
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, 1000);
        channel.Feed(header);

        CancellationTokenSource dripStop = new();
        Task drip = Task.Run(async () =>
        {
            while (!dripStop.IsCancellationRequested)
            {
                channel.Feed(new byte[] { 0x01 });
                await Task.Delay(50);
            }
        });

        try
        {
            await WaitUntilAsync(() => reason != null, TimeSpan.FromSeconds(3));
            Assert.Equal(DisconnectReason.Timeout, reason);
            Assert.IsType<TimeoutException>(error);
        }
        finally
        {
            dripStop.Cancel();
            await drip.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
