using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using Communication.Network.TCP;
using Communication.Shared.Connection;
using Communication.Shared.Messages;
using Communication.Shared.Sessions;

// 사용:
//   Chat.TCP server [port]          — 채팅 서버
//   Chat.TCP client [port] [이름]   — 채팅 클라이언트 (기본 32000 / guest)

int port = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 32000;
string mode;

while (true)
{
    mode = Console.ReadLine();
    
    if (mode == "s")
    {
        await RunServerAsync(port);
    }
    else if (mode == "c")
    {
        string name = args.Length > 2 ? args[2] : "guest";
        await RunClientAsync(port, name);
    }
}

return;

static async Task RunServerAsync(int port)
{
    using TcpListener listener = new(System.Net.IPAddress.Any, port);

    listener.Accepted += channel =>
    {
        TcpSession session = new(channel, new JsonChatConverter(), s => new ChatHandler(s, isServer: true));
        ChatHandler.Join(session);
        session.Disconnected += (_, e) =>
        {
            ChatHandler.Leave(session);
            Console.WriteLine($"client left ({e.Reason}) — total {ChatHandler.RoomCount}");
        };

        Console.WriteLine($"client accepted — total {ChatHandler.RoomCount}");
    };

    listener.Start();
    Console.WriteLine($"server listening on {port} — Ctrl+C to stop");
    await Task.Delay(Timeout.Infinite);
}

static async Task RunClientAsync(int port, string name)
{
    TcpConnector connector = new();
    if (!await connector.ConnectAsync("127.0.0.1", port))
    {
        Console.WriteLine($"connect failed (127.0.0.1:{port})");
        return;
    }

    TcpSession session = new(connector.Channel!, new JsonChatConverter(), s => new ChatHandler(s, isServer: false));
    session.Disconnected += (_, e) => Console.WriteLine($"disconnected: {e.Reason}");
    Console.WriteLine($"connected as {name}. type messages, 'exit' to quit.");

    while (session.IsConnected())
    {
        string? line = Console.ReadLine();
        if (line is null || line == "exit")
        {
            break;
        }

        await session.SendAndFlushAsync(new ChatMessage { Sender = name, Text = line });
    }

    session.Disconnect();
}

/// <summary>샘플용 메시지. 직렬화 포맷은 앱 책임(라이브러리 범위 밖).</summary>
public sealed class ChatMessage
{
    public string Sender { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>샘플 로컬 직렬화기 — JSON. DS_MessageProtocol 주입 자리.</summary>
public sealed class JsonChatConverter : IMessageConverter
{
    public void Serialize(object message, IBufferWriter<byte> writer)
    {
        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType());
        writer.Write(utf8);
    }

    public object Deserialize(ReadOnlySpan<byte> message)
        => JsonSerializer.Deserialize<ChatMessage>(message) ?? new ChatMessage();
}

/// <summary>서버는 수신 메시지를 다른 모두에게 방송하고, 클라이언트는 출력만 한다.</summary>
public sealed class ChatHandler : MessageHandler
{
    private static readonly ConcurrentDictionary<ISession, byte> Room = new();
    private readonly bool _isServer;

    public ChatHandler(ISession session, bool isServer)
        : base(session)
    {
        _isServer = isServer;
        Register<ChatMessage>(OnChat);
    }

    public static int RoomCount => Room.Count;

    public static void Join(ISession session) => Room[session] = 0;

    public static void Leave(ISession session) => Room.TryRemove(session, out _);

    private static void Broadcast(ChatMessage message, ISession? except)
    {
        foreach (ISession peer in Room.Keys)
        {
            if (!ReferenceEquals(peer, except) && peer.IsConnected())
            {
                _ = peer.SendAsync(message);
            }
        }
    }

    private void OnChat(ChatMessage message)
    {
        Console.WriteLine($"[{message.Sender}] {message.Text}");

        if (_isServer)
        {
            Broadcast(message, Session); // 송신자에게는 에코하지 않는다.
        }
    }
}
