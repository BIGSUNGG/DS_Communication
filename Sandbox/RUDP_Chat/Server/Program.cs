using Communication.Shared.Messages;
using Communication.Shared.Session;
using Communication.Network.RUDP.Server;
using Communication.Network.RUDP.Shared.Messages;
using System.Net;

namespace RUDP_Chat.Server;

internal sealed class Program
{
    private static int _nextSessionId;

    private static async Task Main(string[] args)
    {
        var port = ParsePort(args);
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            Console.WriteLine("종료 시그널 감지, 서버를 중지합니다...");
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var listener = new RUDPListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"RUDP 채팅 서버 시작: 0.0.0.0:{port}");

        try
        {
            await listener.ListenAsync((peer, manager, eventListener) => OnClientConnectAsync(peer, manager, eventListener, cts.Token), cts.Token);
        }
        finally
        {
            listener.Stop();
            Console.WriteLine("서버가 종료되었습니다.");
        }
    }

    private static async Task OnClientConnectAsync(LiteNetLib.NetPeer peer, LiteNetLib.NetManager manager, LiteNetLib.EventBasedNetListener listener, CancellationToken cancellationToken)
    {
        var sessionId = Interlocked.Increment(ref _nextSessionId);
        var session = new ClientSession(
            sessionId,
            peer,
            manager,
            (Session s) => { return new RUDPMessageReceiver(peer, manager, listener, new ClientMessageHandler(s)); },
            (Session s) => { return new RUDPMessageSender(peer); }
        );

        ClientSessionManager.Instance.AddSession(session);

        Console.WriteLine($"세션 {sessionId} 접속");

        // 환영 메시지 전송
        await session.SendAsync(new RUDP_Chat.Shared.Messages.S_ChatNotifyMessage("서버", "채팅 서버에 오신 것을 환영합니다!"));
    }

    private static int ParsePort(string[] args)
    {
        const int defaultPort = 5000;
        if (args.Length == 0)
        {
            return defaultPort;
        }

        if (int.TryParse(args[0], out var port) && port is > 0 and < 65535)
        {
            return port;
        }

        Console.WriteLine($"잘못된 포트 값 {args[0]} - 기본 포트 {defaultPort} 사용");
        return defaultPort;
    }
}
