using LiteNetLib;

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
    private CancellationTokenSource? _pollingTokenSource;

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
            _pollingTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

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
            _listener.PeerDisconnectedEvent += (_, _) =>
            {
                if (_connectionTaskSource != null && !_connectionTaskSource.Task.IsCompleted)
                {
                    _connectionTaskSource.SetResult(false);
                }
            };

            _serverPeer = _netManager.Connect(_host, _port, _connectionKey);

            // 연결 이후에도 지속적으로 이벤트를 처리한다.
            _ = Task.Run(() => RunPollingLoopAsync(_pollingTokenSource.Token), _pollingTokenSource.Token);

            // 연결 대기 (최대 5초)
            var timeout = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var completedTask = await Task.WhenAny(_connectionTaskSource.Task, timeout);

            if (completedTask == timeout || (_connectionTaskSource.Task.IsCompleted && !await _connectionTaskSource.Task))
            {
                StopInternal();
                return false;
            }

            if (_serverPeer != null && _serverPeer.ConnectionState == ConnectionState.Connected && _listener != null)
            {
                await _onConnected(_serverPeer, _netManager, _listener);
                return true;
            }

            StopInternal();
            return false;
        }
        catch (OperationCanceledException)
        {
            StopInternal();
            return false;
        }
        catch
        {
            StopInternal();
            return false;
        }
    }

    public void Stop()
    {
        StopInternal();
    }

    private async Task RunPollingLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            _netManager?.PollEvents();

            try
            {
                await Task.Delay(15, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void StopInternal()
    {
        _pollingTokenSource?.Cancel();
        _pollingTokenSource?.Dispose();
        _pollingTokenSource = null;
        if (_netManager != null)
        {
            _netManager.Stop();
            _netManager = null;
        }

        _listener = null;
        _serverPeer = null;
    }
}
