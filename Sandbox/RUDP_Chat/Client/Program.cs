using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using Communication.Network.RUDP.Client;
using Communication.Network.RUDP.Shared.Messages;
using RUDP_Chat.Shared.Messages;

namespace RUDP_Chat.Client;

internal sealed class Program
{
    private static ClientSession? _session;
    private static bool _isRunning = true;

    private static async Task Main(string[] args)
    {
        var host = args.Length > 0 ? args[0] : "localhost";
        var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 5000;

        Console.WriteLine($"서버에 연결 중: {host}:{port}");

        var connector = new RUDPConnector(host, port);
        var messageConverter = new MessageConverter();
        var connected = await connector.ConnectAsync(async (peer, manager, listener) =>
        {
            _session = new ClientSession(
                peer,
                manager,
                s => { return new RUDPMessageReceiver(messageConverter, peer, manager, listener, new ClientMessageHandler(s)); },
                _ => { return new RUDPMessageSender(messageConverter, peer); }
            );

            Console.WriteLine("서버에 연결되었습니다. 채팅을 시작하세요! (종료: 'exit' 입력)");
            Console.WriteLine();

            // 입력 받기 시작
            _ = Task.Run(ReadInputLoop);
            
            await Task.CompletedTask;
        });

        if (!connected)
        {
            Console.WriteLine("서버 연결에 실패했습니다.");
            return;
        }

        // 폴링은 RUDPConnector가 백그라운드에서 처리한다.
        while (_isRunning)
        {
            await Task.Delay(15);
        }

        _session?.Dispose();
        connector.Stop();
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
                _session.Disconnect();
                break;
            }

            var chatMessage = new C_ChatSendMessage(input);
            _ = _session.SendAsync(chatMessage);
        }
    }
}
