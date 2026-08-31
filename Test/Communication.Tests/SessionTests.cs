using Communication.Shared.Connection;
using Communication.Shared.Messages;
using Xunit;

namespace Communication.Tests;

public class SessionTests
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

    [Fact]
    public void NewSession_IsConnected()
    {
        var channel = new FakeByteChannel();
        using var session = new TestSession(channel, new StringConverter(), new RecordingHandler());

        Assert.True(session.IsConnected());
    }

    [Fact]
    public async Task Disconnect_RaisesLocalOnce_AndFaultsFurtherSends()
    {
        var channel = new FakeByteChannel();
        using var session = new TestSession(channel, new StringConverter(), new RecordingHandler());

        int events = 0;
        DisconnectReason? reason = null;
        session.Disconnected += (_, e) =>
        {
            events++;
            reason = e.Reason;
        };

        session.Disconnect();
        session.Disconnect(); // 중복 무시

        Assert.Equal(1, events);
        Assert.Equal(DisconnectReason.Local, reason);
        Assert.False(session.IsConnected());

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync("x"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAndFlushAsync("y"));
    }

    [Fact]
    public async Task RemoteEof_RaisesRemoteDisconnect()
    {
        var channel = new FakeByteChannel();
        using var session = new TestSession(channel, new StringConverter(), new RecordingHandler());

        DisconnectReason? reason = null;
        session.Disconnected += (_, e) => reason = e.Reason;

        channel.Complete();
        await WaitUntilAsync(() => reason != null);

        Assert.Equal(DisconnectReason.Remote, reason);
        Assert.False(session.IsConnected());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync("x"));
    }

    [Fact]
    public void Dispose_CountsAsLocalDisconnect()
    {
        var channel = new FakeByteChannel();
        var session = new TestSession(channel, new StringConverter(), new RecordingHandler());

        DisconnectReason? reason = null;
        session.Disconnected += (_, e) => reason = e.Reason;

        session.Dispose();

        Assert.Equal(DisconnectReason.Local, reason);
        Assert.False(session.IsConnected());
    }

    [Fact]
    public async Task UnattachedSession_Send_FaultsTask_WithoutSynchronousThrow()
    {
        var channel = new FakeByteChannel();
        using var session = new UnattachedTestSession(channel);

        // 호출 자체는 던지지 않고, 예외로 완료된 Task를 돌려준다.
        Task send = session.SendAsync("x");
        await Assert.ThrowsAsync<InvalidOperationException>(() => send);

        Task flush = session.SendAndFlushAsync("y");
        await Assert.ThrowsAsync<InvalidOperationException>(() => flush);

        Assert.False(session.IsConnected());
    }

    [Fact]
    public void ThrowingDisconnectedSubscriber_DoesNotBlockOthers()
    {
        var channel = new FakeByteChannel();
        using var session = new TestSession(channel, new StringConverter(), new RecordingHandler());

        bool secondCalled = false;
        session.Disconnected += (_, _) => throw new InvalidOperationException("subscriber exploded");
        session.Disconnected += (_, _) => secondCalled = true;

        session.Disconnect();

        Assert.True(secondCalled); // 던지는 구독자도 나머지를 건너뛰지 못한다
    }
}
