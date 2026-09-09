using System.Net;
using System.Net.Sockets;
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

    /// <summary>
    /// 동시 접속 폭풍 회귀 — 한 wave에 16개 연결이 동시에 열리고 절반은 즉시 이탈(절반은 RST 강제),
    /// 절반은 왕복 후 정상 정리된다. 매 wave 슬롯 0 회복 + 폭풍 뒤 서버 생존 + 리스너 정지 후
    /// 같은 포트 재바인딩(서버 선행 종료 소켓이 TIME_WAIT에 남으면 OS가 일시 거부한다 — 15초 한도 내 재시기)을 검증한다.
    /// 동시 RST 청urn은 수용 직후 끊긴 연결의 소켓 옵션 적용 경합(수용 루프 생존성)을 확률적으로 노출시킨다.
    /// </summary>
    [Fact]
    public async Task Tcp_ConnectStorm_MixedDrops_ServerSurvives_SlotsRecover_PortRebinds()
    {
        const int waves = 3;
        const int burst = 16; // 상한과 동일 — 거부 모호성 없이 전량 수용
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Accepted += channel => _ = new TcpSession(channel, new StringConverter(), s => new TagEchoHandler(s));
        listener.Start(new TcpTransportOptions { MaxConnections = burst });
        int port = ((IPEndPoint)listener.LocalEndpoint!).Port;

        for (int wave = 0; wave < waves; wave++)
        {
            Task[] tasks = Enumerable.Range(0, burst).Select(i => RoundAsync(wave, i)).ToArray();
            await Task.WhenAll(tasks);
            await WaitUntilAsync(() => listener.ActiveConnectionCount == 0, 15000); // 매 wave 완전 회복
        }

        // 폭풍 뒤 생존 — 서버는 계속 수용·왕복해야 한다
        await EchoRoundAsync("127.0.0.1", port, "alive", 10000);

        // 포트 재사용 — 리스너를 정지하고 방금 놓은 포트에 재바인딩 후 왕복한다. 서버가 먼저 닫은
        // 소켓이 TIME_WAIT에 남으면 OS가 재바인딩을 일시 거부한다(SO_REUSEADDR 미설정) —
        // 15초 한도 내 재시기로 회복을 기다리고, 못 얻으면 실패로 확정한다.
        listener.Dispose();
        TcpListener rebinder = StartOnPortWithRetry(IPAddress.Loopback, port, new TcpTransportOptions(), 15000);
        try
        {
            await EchoRoundAsync("127.0.0.1", port, "rebind", 10000);
        }
        finally
        {
            rebinder.Dispose();
        }

        return;

        async Task RoundAsync(int wave, int i)
        {
            var connector = new TcpConnector();
            Assert.True(await connector.ConnectAsync("127.0.0.1", port), $"wave {wave} 클라 {i} 연결 실패");

            if ((wave + i) % 2 == 0)
            {
                // 즉시 이탈 — 짝수 인덱스 절반은 RST(abortive), 나머지 절반은 정상 FIN
                if ((wave + i) % 4 == 0)
                {
                    ((StreamByteChannel)connector.Channel!).Socket.LingerState = new LingerOption(true, 0);
                }

                connector.Channel!.Dispose();
                return;
            }

            Collector? collector = null;
            using TcpSession session = new(connector.Channel!, new StringConverter(), s =>
            {
                collector = new Collector(s);
                return collector;
            });

            string tag = $"w{wave}c{i}";
            await session.SendAndFlushAsync(tag);
            await WaitUntilAsync(() => collector!.Messages.Count > 0);
            Assert.Equal(new[] { tag }, collector!.Messages); // 세션 간 혼입 없음
        }

        static async Task EchoRoundAsync(string host, int targetPort, string tag, int timeoutMs)
        {
            var connector = new TcpConnector();
            Assert.True(await connector.ConnectAsync(host, targetPort));
            Collector? collector = null;
            using TcpSession session = new(connector.Channel!, new StringConverter(), s =>
            {
                collector = new Collector(s);
                return collector;
            });
            await session.SendAndFlushAsync(tag);
            await WaitUntilAsync(() => collector!.Messages.Count > 0, timeoutMs);
            Assert.Equal(new[] { tag }, collector!.Messages);
        }

        static TcpListener StartOnPortWithRetry(IPAddress address, int port, TcpTransportOptions options, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                var candidate = new TcpListener(address, port);
                candidate.Accepted += channel => _ = new TcpSession(channel, new StringConverter(), s => new TagEchoHandler(s));
                try
                {
                    candidate.Start(options);
                    return candidate;
                }
                catch (SocketException)
                {
                    candidate.Dispose();
                    if (DateTime.UtcNow > deadline)
                    {
                        throw new TimeoutException($"포트 {port} 재바인딩이 {timeoutMs}ms 안에 성공하지 못했다(TIME_WAIT 등 소켓 점유).");
                    }

                    Thread.Sleep(200);
                }
            }
        }
    }

    /// <summary>
    /// RUDP 동시 접속 폭풍 + 상한 거부 청urn — 상한(4)보다 많은 동시 요청(10)이 섞이면
    /// 수용·거부·즉시 이탈·왕복이 뒤섞인다. 모든 결과가 명확히 확정되고(무응답 없음) 슬롯이 매 wave 0으로
    /// 회복되며, 폭풍 뒤 서버 생존·포트 재바인딩이 되는지 검증한다.
    /// </summary>
    [Fact]
    public async Task Rudp_ConnectStorm_RejectionsAndChurn_SlotsRecover_PortRebinds()
    {
        const int waves = 3;
        const int burst = 10;
        const int cap = 4; // burst보다 작게 — 거부 경로 강제
        var listener = new Communication.Network.RUDP.RudpListener(IPAddress.Loopback, 0);
        listener.Accepted += channel => _ = new Communication.Network.RUDP.RudpSession(channel, new StringConverter(), s => new TagEchoHandler(s));
        listener.Start(new Communication.Network.RUDP.RudpTransportOptions { MaxConnections = cap });
        int port = listener.LocalPort;

        int connected = 0;
        int rejected = 0;
        try
        {
            for (int wave = 0; wave < waves; wave++)
            {
                Task[] tasks = Enumerable.Range(0, burst).Select(i => RoundAsync(wave, i)).ToArray();
                await Task.WhenAll(tasks);
                await WaitUntilAsync(() => listener.ActiveConnectionCount == 0, 15000); // 거부·즉시 이탈 섞인 뒤 완전 회복
            }

            Assert.True(connected > 0, "폭풍 중 수용된 연결이 하나도 없음 — 폭풍 시나리오 오류");
            Assert.True(rejected > 0, "거부된 연결이 하나도 없음 — 상한 강제가 안 걸림(버스트가 상한을 안 넘음)");

            // 폭풍 뒤 생존
            await EchoRoundAsync(port, "alive", 15000);
        }
        finally
        {
            listener.Stop();
        }

        // 포트 재사용 — 방금 놓은 포트에 즉시 재바인딩 후 왕복
        var rebinder = new Communication.Network.RUDP.RudpListener(IPAddress.Loopback, port);
        rebinder.Accepted += channel => _ = new Communication.Network.RUDP.RudpSession(channel, new StringConverter(), s => new TagEchoHandler(s));
        rebinder.Start(new Communication.Network.RUDP.RudpTransportOptions());
        try
        {
            await EchoRoundAsync(port, "rebind", 15000);
        }
        finally
        {
            rebinder.Stop();
        }

        return;

        async Task RoundAsync(int wave, int i)
        {
            var connector = new Communication.Network.RUDP.RudpConnector();
            bool ok = await connector.ConnectAsync("127.0.0.1", port);
            if (!ok)
            {
                Interlocked.Increment(ref rejected); // 상한 초과 거부 — 폭풍 중 정상 결과
                return;
            }

            Interlocked.Increment(ref connected);
            if ((wave + i) % 2 == 0)
            {
                connector.Channel!.Dispose(); // 즉시 이탈 — 세션 없이 채널 폐기
                return;
            }

            Collector? collector = null;
            using Communication.Network.RUDP.RudpSession session = new(
                connector.Channel!, new StringConverter(), s =>
                {
                    collector = new Collector(s);
                    return collector;
                });

            string tag = $"w{wave}c{i}";
            await session.SendAndFlushAsync(tag);
            await WaitUntilAsync(() => collector!.Messages.Count > 0);
            Assert.Equal(new[] { tag }, collector!.Messages);
        }

        static async Task EchoRoundAsync(int targetPort, string tag, int timeoutMs)
        {
            var connector = new Communication.Network.RUDP.RudpConnector();
            Assert.True(await connector.ConnectAsync("127.0.0.1", targetPort));
            Collector? collector = null;
            using Communication.Network.RUDP.RudpSession session = new(
                connector.Channel!, new StringConverter(), s =>
                {
                    collector = new Collector(s);
                    return collector;
                });
            await session.SendAndFlushAsync(tag);
            await WaitUntilAsync(() => collector!.Messages.Count > 0, timeoutMs);
            Assert.Equal(new[] { tag }, collector!.Messages);
        }
    }
}
