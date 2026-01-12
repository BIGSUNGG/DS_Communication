using LiteNetLib;
using System.Net;
using System.Net.Sockets;

namespace Communication.Network.RUDP.Client;

public sealed class RUDPConnector
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _connectionKey;
    private NetManager? _netManager;
    private NetPeer? _serverPeer;
    private EventBasedNetListener? _listener;
    private Func<NetPeer, NetManager, EventBasedNetListener, Task>? _onConnected;
    private TaskCompletionSource<bool>? _connectionTaskSource;

    public RUDPConnector(string host, int port, string connectionKey = "")
    {
        _host = host;
        _port = port;
        _connectionKey = connectionKey;
    }

    public async Task<bool> ConnectAsync(Func<NetPeer, NetManager, EventBasedNetListener, Task> onConnected, CancellationToken cancellationToken = default)
    {
        try
        {
            _onConnected = onConnected;
            _connectionTaskSource = new TaskCompletionSource<bool>();

            _listener = new EventBasedNetListener();
            _netManager = new NetManager(_listener);
            _netManager.Start();

            // 연결 성공 이벤트
            _listener.PeerConnectedEvent += (peer) =>
            {
                _serverPeer = peer;
                if (_connectionTaskSource != null && !_connectionTaskSource.Task.IsCompleted)
                {
                    _connectionTaskSource.SetResult(true);
                }
            };

            // 연결 실패 이벤트
            _listener.PeerDisconnectedEvent += (peer, disconnectInfo) =>
            {
                if (_connectionTaskSource != null && !_connectionTaskSource.Task.IsCompleted)
                {
                    _connectionTaskSource.SetResult(false);
                }
            };

            _serverPeer = _netManager.Connect(_host, _port, _connectionKey);

            // PollEvents를 백그라운드에서 계속 호출
            var pollTask = Task.Run(async () =>
            {
                while (!_connectionTaskSource.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
                {
                    _netManager?.PollEvents();
                    await Task.Delay(15, cancellationToken);
                }
            }, cancellationToken);

            // 연결 대기 (최대 5초)
            var timeout = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var completedTask = await Task.WhenAny(_connectionTaskSource.Task, timeout);

            // pollTask는 백그라운드에서 계속 실행되므로 여기서는 무시

            if (completedTask == timeout || (_connectionTaskSource.Task.IsCompleted && !await _connectionTaskSource.Task))
            {
                if (_netManager != null)
                {
                    _netManager.Stop();
                    _netManager = null;
                }
                _listener = null;
                return false;
            }

            if (_serverPeer != null && _serverPeer.ConnectionState == ConnectionState.Connected && _listener != null)
            {
                await _onConnected(_serverPeer, _netManager, _listener);
                return true;
            }

            if (_netManager != null)
            {
                _netManager.Stop();
                _netManager = null;
            }
            _listener = null;
            return false;
        }
        catch (OperationCanceledException)
        {
            if (_netManager != null)
            {
                _netManager.Stop();
                _netManager = null;
            }
            _listener = null;
            return false;
        }
        catch
        {
            if (_netManager != null)
            {
                _netManager.Stop();
                _netManager = null;
            }
            _listener = null;
            return false;
        }
    }
}
