using Communication.Shared.Messages;
using Communication.Shared.Session;
using Communication.Network.TCP.Server;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DB.EF_Core;
using Microsoft.EntityFrameworkCore;

namespace Server;

internal sealed class Program
{
    private static int _nextSessionId;

    private static async Task Main(string[] args)
    {
        // DB 초기화
        InitializeDatabase();

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
        Console.WriteLine($"TCP 채팅 서버 시작: 0.0.0.0:{port}");

        try
        {
            await listener.ListenAsync(client => OnClientConnectAsync(client, cts.Token), cts.Token);
        }
        finally
        {
            listener.Stop();
            Console.WriteLine("서버가 종료되었습니다.");
        }
    }

    private static async Task OnClientConnectAsync(TcpClient client, CancellationToken cancellationToken)
    {
        // KeepAlive 옵션 설정
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        
        var sessionId = Interlocked.Increment(ref _nextSessionId);
        var session = new ClientSession(
            sessionId,
            client,
            (Session s) => { return new TCPMessageReceiver(client.GetStream(), new ClientMessageHandler(s)); },
            (Session s) => { return new TCPMessageSender(client.GetStream()); }
        );

        ClientSessionManager.Instance.AddSession(session);

        Console.WriteLine($"세션 {sessionId} 접속: {client.Client.RemoteEndPoint}");

        // 로그인 전에는 환영 메시지만 전송
        await session.SendAsync(new S_ChatNotifyMessage("서버", "채팅 서버에 오신 것을 환영합니다! 로그인 후 채팅을 이용하실 수 있습니다."));
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

    private static void InitializeDatabase()
    {
        try
        {
            using var context = new SQLiteDbContext();
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
            Console.WriteLine("데이터베이스 초기화 완료");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"데이터베이스 초기화 실패: {ex.Message}");
        }
    }
}

