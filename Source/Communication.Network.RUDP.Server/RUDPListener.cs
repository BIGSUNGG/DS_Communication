using LiteNetLib;
using System.Net;
using System.Net.Sockets;

namespace Communication.Network.RUDP.Server;

public sealed class RUDPListener
{
    private IPAddress _ipAddress;
    private int _port;
    private string _connectionKey;

    private readonly NetManager _netManager;
    private readonly EventBasedNetListener _listener;
    private Func<NetPeer, NetManager, EventBasedNetListener, Task>? _onClientAccepted;
    private CancellationToken _cancellationToken;

    public RUDPListener(IPAddress ipAddress, int port, string connectionKey = "")
    {
        _ipAddress = ipAddress;
        _port = port;
        _connectionKey = connectionKey;

        _listener = new EventBasedNetListener();
        _netManager = new NetManager(_listener);  

        // 연결 요청 수락
        _listener.ConnectionRequestEvent += (request) =>
        {
            request.Accept();
        };

        // 클라이언트 연결 이벤트
        _listener.PeerConnectedEvent += (peer) =>
        {
            if (_onClientAccepted != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _onClientAccepted(peer, _netManager, _listener);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error handling client connection: {ex.Message}");
                    }
                }, _cancellationToken);
            }
        };      
    }

    public void Start()
    {
        _netManager.Start(_port);
    }

    public void Stop()
    {
        _netManager.Stop();
    }

    public async Task ListenAsync(Func<NetPeer, NetManager, EventBasedNetListener, Task> onClientAccepted, CancellationToken token)
    {
        _onClientAccepted = onClientAccepted;
        _cancellationToken = token;

        while (!token.IsCancellationRequested)
        {
            _netManager.PollEvents();
            await Task.Delay(15, token);
        }
    }
}
