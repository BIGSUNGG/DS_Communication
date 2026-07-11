using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using Communication.Network.TCP_IOCP.Server;
using Communication.Network.TCP_IOCP.Shared.Messages;
using TCP_IOCP_Chat.Shared.Messages;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TCP_IOCP_Chat.Server;

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

        var listener = new TCPListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine($"TCP_IOCP 채팅 서버 시작: 0.0.0.0:{port}");

        try
        {
           await listener.ListenAsync(socket => OnClientConnectAsync(socket, cts.Token), cts.Token);
        }
        finally
        {
            listener.Stop();
            Console.WriteLine("서버가 종료되었습니다.");
        }
    }

    private static async Task OnClientConnectAsync(Socket socket, CancellationToken cancellationToken)
    {
        var sessionId = Interlocked.Increment(ref _nextSessionId);
        var messageConverter = new MessageConverter();

        var session = new ClientSession(
            sessionId,
            socket,
            (Session s) => new TCPMessageReceiver(messageConverter, socket, new ClientMessageHandler(s)),
            (Session s) => new TCPMessageSender(messageConverter, socket)
        );

        ClientSessionManager.Instance.AddSession(session);

        Console.WriteLine($"세션 {sessionId} 접속");

        // 환영 메시지 전송
        await session.SendAsync(new S_ChatNotifyMessage("서버", "채팅 서버에 오신 것을 환영합니다!"));
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

