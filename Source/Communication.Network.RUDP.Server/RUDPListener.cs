using Communication.Network.RUDP.Shared.Messages;
using LiteNetLib;
using System.Net;

namespace Communication.Network.RUDP.Server;

public sealed class RUDPListener : IDisposable
{
    private IPAddress _ipAddress;
    private int _port;
    private string _connectionKey;

    private readonly NetManager _netManager;
    private readonly EventBasedNetListener _listener;
    private readonly RUDPNetworkReceiveDispatcher _receiveDispatcher;
    private Func<NetPeer, NetManager, EventBasedNetListener, RUDPNetworkReceiveDispatcher, Task>? _onClientAccepted;
    private bool _stopped;

    public RUDPNetworkReceiveDispatcher ReceiveDispatcher => _receiveDispatcher;

    public RUDPListener(IPAddress ipAddress, int port, string connectionKey = "")
    {
        _ipAddress = ipAddress;
        _port = port;
        _connectionKey = connectionKey;

        _listener = new EventBasedNetListener();
        _receiveDispatcher = new RUDPNetworkReceiveDispatcher(_listener);
        _netManager = new NetManager(_listener);

        _listener.ConnectionRequestEvent += (request) =>
        {
            request.AcceptIfKey(_connectionKey);
        };

        _listener.PeerConnectedEvent += (peer) =>
        {
            if (_onClientAccepted != null)
            {
                _ = HandleClientAcceptedAsync(peer);
            }
        };
    }

    private async Task HandleClientAcceptedAsync(NetPeer peer)
    {
        try
        {
            if (_onClientAccepted != null)
            {
                await _onClientAccepted(peer, _netManager, _listener, _receiveDispatcher).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling client connection: {ex.Message}");
        }
    }

    public void Start()
    {
        _netManager.Start(_port);
    }

    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        _netManager.Stop();
        _receiveDispatcher.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }

    public async Task ListenAsync(Func<NetPeer, NetManager, EventBasedNetListener, RUDPNetworkReceiveDispatcher, Task> onClientAccepted, CancellationToken token)
    {
        _onClientAccepted = onClientAccepted;

        while (!token.IsCancellationRequested)
        {
            _netManager.PollEvents();
            await Task.Delay(1, token).ConfigureAwait(false);
        }
    }
}
