using System.Net;
using Communication.Network.TCP;
using Communication.Shared.Channels;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using Xunit;
using TcpListener = Communication.Network.TCP.TcpListener;

namespace Communication.Tests;

/// <summary>
/// 연결 청urn(churn) 소크 회귀 — 지속 반복 접속·왕복·단절에서 리스너 슬롯이 매 라운드 0으로 회복되는지
/// (누수 탐지기 = ActiveConnectionCount), 라운드 태그 메시지가 세션 간 섞이지 않는지(정합성) 검증한다.
/// 단발 테스트가 놓치는 누적 경합(확률적 슬롯 누수 등)을 상한이 촘촘한 MaxConnections로 조기 노출시킨다.
/// </summary>
[Collection("network-loopback")]
public class ChurnSoakTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("조건이 시간 안에 만족되지 않았습니다.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class TagEchoHandler : MessageHandler
    {
        public TagEchoHandler(ISession session)
            : base(session)
        {
            Register<string>(m => _ = Session.SendAsync(m)); // 에코 — 라운드 태그 보존
        }
    }

    private sealed class Collector : MessageHandler
    {
        private readonly List<string> _messages = new();

        public Collector(ISession session)
            : base(session)
        {
            Register<string>(m =>
            {
                lock (_messages)
                {
                    _messages.Add(m);
                }
            });
        }

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToList();
                }
            }
        }
    }

    [Fact]
    public async Task Tcp_Churn_SlotsRecoverEveryRound_AndTagsStayIntact()
    {
        const int rounds = 60;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Accepted += channel => _ = new TcpSession(channel, new StringConverter(), s => new TagEchoHandler(s));
        listener.Start(new TcpTransportOptions { MaxConnections = 8 }); // 촘촘한 상한 — 누수 시 조기 실패
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

        for (int i = 0; i < rounds; i++)
        {
            var connector = new TcpConnector();
            Assert.True(await connector.ConnectAsync("127.0.0.1", port), $"라운드 {i} 연결 실패");
            Collector? collector = null;
            using TcpSession session = new(connector.Channel!, new StringConverter(), s =>
            {
                collector = new Collector(s);
                return collector;
            });

            string tag = $"r{i}";
            await session.SendAndFlushAsync(tag);
            await WaitUntilAsync(() => collector!.Messages.Count > 0);
            Assert.Equal(new[] { tag }, collector!.Messages); // 태그 단독 — 혼입·중복 없음(이름이 주장하는 바, 강화)

            session.Dispose(); // 로컬 단절 → 서버 채널 정리 → 슬롯 회수
            await WaitUntilAsync(() => listener.ActiveConnectionCount == 0); // 매 라운드 누수 단언
        }

        Assert.Equal(0, listener.ActiveConnectionCount);
    }

    [Fact]
    public async Task Rudp_Churn_SlotsRecoverEveryRound_AndTagsStayIntact()
    {
        const int rounds = 40;
        using var listener = new Communication.Network.RUDP.RudpListener(IPAddress.Loopback, 0);
        listener.Accepted += channel => _ = new Communication.Network.RUDP.RudpSession(channel, new StringConverter(), s => new TagEchoHandler(s));
        listener.Start(new Communication.Network.RUDP.RudpTransportOptions { MaxConnections = 4 });
        int port = listener.LocalPort;

        for (int i = 0; i < rounds; i++)
        {
            var connector = new Communication.Network.RUDP.RudpConnector();
            Assert.True(await connector.ConnectAsync("127.0.0.1", port), $"라운드 {i} 연결 실패");
            Collector? collector = null;
            using Communication.Network.RUDP.RudpSession session = new(
                connector.Channel!, new StringConverter(), s =>
                {
                    collector = new Collector(s);
                    return collector;
                });

            string tag = $"r{i}";
            await session.SendAndFlushAsync(tag);
            await WaitUntilAsync(() => collector!.Messages.Count > 0);
            Assert.Equal(new[] { tag }, collector!.Messages); // 태그 단독 — 혼입·중복 없음(이름이 주장하는 바, 강화)

            session.Dispose();
            await WaitUntilAsync(() => listener.ActiveConnectionCount == 0);
        }

        Assert.Equal(0, listener.ActiveConnectionCount);
    }
}
