using Communication.Network.TCP_IOCP.Client;
using Communication.Shared.Sessions;
using Communication.Shared.Messages;
using Communication.Network.TCP_IOCP.Shared.Messages;
using TCP_IOCP_Chat.Shared.Messages;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TCP_IOCP_Chat.Client;

internal sealed class Program
{
    private static ClientSession? _session;
    private static bool _isRunning = true;

    private static async Task Main(string[] args)
    {
        var host = args.Length > 0 ? args[0] : "localhost";
        var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 5000;

        Console.WriteLine($"서버에 연결 중: {host}:{port}");

        var connector = new TCPConnector(host, port);
        var messageConverter = new MessageConverter();

        var connected = await connector.ConnectAsync(async (socket) =>
        {
            _session = new ClientSession(
                socket,
                (Session s) => new TCPMessageReceiver(messageConverter, socket, new ClientMessageHandler(s)),
                (Session s) => new TCPMessageSender(messageConverter, socket)
            );

            Console.WriteLine("서버에 연결되었습니다. 채팅을 시작하세요! (종료: 'exit' 입력)");
            Console.WriteLine();

            _ = Task.Run(ReadInputLoop);

            await Task.CompletedTask;
        });

        if (!connected)
        {
            Console.WriteLine("서버 연결에 실패했습니다.");
            return;
        }

        while (_isRunning && _session != null && _session.IsConnected())
        {
            await Task.Delay(100);
        }

        _session?.Dispose();
    }

    private static void ReadInputLoop()
    {
        while (_isRunning && _session != null)
        {
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                _isRunning = false;
                _session?.Disconnect();
                break;
            }

            if (_session != null)
            {
                var chatMessage = new C_ChatSendMessage(input);
                _ = _session.SendAsync(chatMessage);
            }
        }
    }
}

