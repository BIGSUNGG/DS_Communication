using Communication.Shared.Messages;
using Communication.Shared.Sessions;
using Communication.TCP.Shared.Messages;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Bot;

/// <summary>
/// 단일 Bot 클라이언트
/// - Connect -> (선택) Register -> Login -> Chat 전송 테스트
/// - 응답을 Task 로 기다릴 수 있도록 구현
/// </summary>
public sealed class BotClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly TcpClient _tcpClient;
    private BotSession? _session;

    private readonly TaskCompletionSource<S_LoginResponseMessage> _loginTcs = new();
    private readonly TaskCompletionSource<S_RegisterResponseMessage> _registerTcs = new();

    public string Name { get; }

    public BotClient(string name, string host, int port)
    {
        Name = name;
        _host = host;
        _port = port;
        _tcpClient = new TcpClient();
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Console.WriteLine($"[Bot:{Name}] Connecting to {_host}:{_port}...");
            await _tcpClient.ConnectAsync(_host, _port, cancellationToken);

            var messageConverter = new MessageConverter();
            var session = new BotSession(
                Name,
                _tcpClient,
                s => new TCPMessageReceiver(messageConverter, _tcpClient.GetStream(), new BotMessageHandler(s, this)),
                s => new TCPMessageSender(messageConverter, _tcpClient.GetStream()));

            _session = session;

            Console.WriteLine($"[Bot:{Name}] Connected.");
            return true;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[Bot:{Name}] Connect canceled.");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Bot:{Name}] Connect failed: {ex.Message}");
            return false;
        }
    }

    public async Task<S_RegisterResponseMessage?> RegisterAsync(string userId, string password)
    {
        if (_session == null)
        {
            Console.WriteLine($"[Bot:{Name}] Register failed: not connected.");
            return null;
        }

        Console.WriteLine($"[Bot:{Name}] Register request: {userId}");
        await _session.SendAsync(new C_RegisterRequestMessage(userId, password));
        return await _registerTcs.Task;
    }

    public async Task<S_LoginResponseMessage?> LoginAsync(string userId, string password)
    {
        if (_session == null)
        {
            Console.WriteLine($"[Bot:{Name}] Login failed: not connected.");
            return null;
        }

        Console.WriteLine($"[Bot:{Name}] Login request: {userId}");
        await _session.SendAsync(new C_LoginRequestMessage(userId, password));
        return await _loginTcs.Task;
    }

    public async Task SendChatAsync(string content)
    {
        if (_session == null)
        {
            Console.WriteLine($"[Bot:{Name}] Chat send failed: not connected.");
            return;
        }

        Console.WriteLine($"[Bot:{Name}] SendChat: {content}");
        await _session.SendAsync(new C_ChatSendMessage(content));
    }

    // BotMessageHandler 에서 호출
    internal void CompleteLogin(S_LoginResponseMessage response)
    {
        if (!_loginTcs.Task.IsCompleted)
        {
            _loginTcs.TrySetResult(response);
        }
    }

    internal void CompleteRegister(S_RegisterResponseMessage response)
    {
        if (!_registerTcs.Task.IsCompleted)
        {
            _registerTcs.TrySetResult(response);
        }
    }

    internal void OnChatNotified(S_ChatNotifyMessage message)
    {
        // 현재는 콘솔 출력만, 필요하면 콜백/이벤트로 확장 가능
    }

    public void Dispose()
    {
        _session?.Disconnect();
        _session = null;
        _tcpClient.Dispose();
    }
}


