using System.Net;
using Communication.Network.RUDP;
using Communication.Shared.Connection;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using Xunit;

namespace Communication.Tests;

/// <summary>
/// 리스너 정지(Stop/Dispose)가 살아있는 서버 세션에 로컬 종료를 통지하는지 —
/// 폴링 스레드가 멈추면 NetManager 이벤트는 드레인되지 않으므로 호스트가 직접 통지해야 한다.
/// </summary>
[Collection("network-loopback")]
public class RudpListenerStopTests
{
    [Fact]
    public async Task ListenerStop_NotifiesLiveServerSessions_WithLocalReason()
    {
        using var listener = new RudpListener(IPAddress.Loopback, 0);

        RudpSession? serverSession = null;
        var serverDisconnected = new TaskCompletionSource<DisconnectReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.Accepted += channel =>
        {
            serverSession = new RudpSession(channel, new StringConverter(), s =>
            {
                s.Disconnected += (_, e) => serverDisconnected.TrySetResult(e.Reason);
                return new CollectHandlerStub(s);
            });
        };
        listener.Start();
        int port = listener.LocalPort;

        var connector = new RudpConnector();
        Assert.True(await connector.ConnectAsync("127.0.0.1", port));
        using RudpSession clientSession = new(connector.Channel!, new StringConverter(), s => new CollectHandlerStub(s));

        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (serverSession is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.NotNull(serverSession);
        Assert.True(serverSession!.IsConnected());

        listener.Stop(); // 호스트 정지 — 서버 세션에 Local 통지가 와야 한다

        DisconnectReason reason = await serverDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DisconnectReason.Local, reason);
        Assert.False(serverSession.IsConnected());
    }

    private sealed class CollectHandlerStub : MessageHandler
    {
        public CollectHandlerStub(ISession session)
            : base(session)
        {
        }
    }
}
