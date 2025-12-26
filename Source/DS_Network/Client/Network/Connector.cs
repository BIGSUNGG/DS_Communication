using System.Net;
using System.Net.Sockets;

namespace Client;

public sealed class Connector
{
    private readonly string _host;
    private readonly int _port;

    public Connector(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async Task<bool> ConnectAsync(Func<TcpClient, Task> onConnected, CancellationToken cancellationToken = default)
    {
        try
        {
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(_host, _port, cancellationToken);
            await onConnected(tcpClient);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            // 연결 실패
            return false;
        }
    }
}

